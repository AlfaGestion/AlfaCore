import fsp from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import makeWASocket, {
  Browsers,
  DisconnectReason,
  fetchLatestBaileysVersion,
  useMultiFileAuthState
} from "@whiskeysockets/baileys";
import pino from "pino";

const [, , command = "", sessionDirArg = "", modeArg = "QR", phoneArg = "", includeCodeArg = "0", instanceArg = ""] = process.argv;

if (command !== "start" || !sessionDirArg) {
  console.error("Usage: node worker.mjs start <sessionDir> <mode> <phone> <includeCode> <instanceName>");
  process.exit(1);
}

const sessionDir = path.resolve(sessionDirArg);
const authDir = path.join(sessionDir, "auth");
const statusFile = path.join(sessionDir, "status.json");
const inboxDir = path.join(sessionDir, "inbox");
const outboxDir = path.join(sessionDir, "outbox");
const resultsDir = path.join(sessionDir, "results");
const acksDir = path.join(sessionDir, "acks");
const mode = String(modeArg || "QR").toUpperCase() === "PHONE_NUMBER" ? "PHONE_NUMBER" : "QR";
const phone = String(phoneArg || "").replace(/\D/g, "");
const includeTextCode = includeCodeArg === "1";
const instanceName = String(instanceArg || path.basename(sessionDir));

await fsp.mkdir(authDir, { recursive: true });
await fsp.mkdir(inboxDir, { recursive: true });
await fsp.mkdir(outboxDir, { recursive: true });
await fsp.mkdir(resultsDir, { recursive: true });
await fsp.mkdir(acksDir, { recursive: true });

let sock;
let isSocketOpen = false;
let pairingRequested = false;
let isStopping = false;
let isProcessingOutbox = false;
let reconnectTimer = null;
let reconnectAttempt = 0;

process.on("SIGTERM", async () => {
  isStopping = true;
  clearReconnectTimer();
  await writeStatus({
    state: "STOPPED",
    sessionId: instanceName,
    processId: process.pid,
    lastUpdatedAtUtc: new Date().toISOString()
  });
  process.exit(0);
});

process.on("SIGINT", async () => {
  isStopping = true;
  clearReconnectTimer();
  await writeStatus({
    state: "STOPPED",
    sessionId: instanceName,
    processId: process.pid,
    lastUpdatedAtUtc: new Date().toISOString()
  });
  process.exit(0);
});

await writeStatus({
  state: "STARTING",
  sessionId: instanceName,
  processId: process.pid,
  lastUpdatedAtUtc: new Date().toISOString()
});

await connectSocket();

setInterval(async () => {
  if (isStopping) {
    return;
  }

  await processOutbox();
}, 1200);

setInterval(async () => {
  if (isStopping) {
    return;
  }

  const status = await readStatus();
  await writeStatus({
    ...status,
    state: status.state || "RUNNING",
    sessionId: instanceName,
    processId: process.pid,
    lastUpdatedAtUtc: new Date().toISOString()
  });
}, 15000);

async function connectSocket() {
  try {
    pairingRequested = false;
    const { state, saveCreds } = await useMultiFileAuthState(authDir);
    const { version } = await fetchLatestBaileysVersion();

    sock = makeWASocket({
      version,
      auth: state,
      browser: Browsers.macOS("Desktop"),
      logger: pino({ level: "silent" }),
      printQRInTerminal: false,
      markOnlineOnConnect: false,
      syncFullHistory: false
    });

    sock.ev.on("creds.update", saveCreds);
    sock.ev.on("messages.upsert", handleMessagesUpsert);
    sock.ev.on("messages.update", handleMessagesUpdate);
    sock.ev.on("connection.update", handleConnectionUpdate);
  } catch (error) {
    const errorMessage = serializeError(error);
    await writeStatus({
      state: "RECONNECTING",
      sessionId: instanceName,
      processId: process.pid,
      error: errorMessage,
      lastUpdatedAtUtc: new Date().toISOString()
    });
    scheduleReconnect();
  }
}

async function handleConnectionUpdate(update) {
  const { connection, lastDisconnect, qr } = update;
  const now = new Date();

  if (qr) {
    await writeStatus({
      state: "QR_READY",
      sessionId: instanceName,
      processId: process.pid,
      qrPayload: qr,
      generatedAtUtc: now.toISOString(),
      expiresAtUtc: new Date(now.getTime() + 2 * 60 * 1000).toISOString(),
      lastUpdatedAtUtc: now.toISOString(),
      error: ""
    });

    if (mode === "PHONE_NUMBER" && includeTextCode && phone && !pairingRequested && !(sock?.authState?.creds?.registered)) {
      pairingRequested = true;
      try {
        const pairingCode = await sock.requestPairingCode(phone);
        await writeStatus({
          state: "PAIRING_CODE_READY",
          sessionId: instanceName,
          processId: process.pid,
          qrPayload: qr,
          pairingCode,
          generatedAtUtc: now.toISOString(),
          expiresAtUtc: new Date(now.getTime() + 2 * 60 * 1000).toISOString(),
          lastUpdatedAtUtc: new Date().toISOString(),
          error: ""
        });
      } catch (error) {
        await writeStatus({
          state: "ERROR",
          sessionId: instanceName,
          processId: process.pid,
          error: serializeError(error),
          lastUpdatedAtUtc: new Date().toISOString()
        });
      }
    }
  }

  if (connection === "open") {
    isSocketOpen = true;
    reconnectAttempt = 0;
    clearReconnectTimer();
    const confirmedJid = String(sock?.user?.id || "");
    const confirmedPhoneDigits = confirmedJid.split("@")[0]?.split(":")[0]?.replace(/\D/g, "") || "";
    await writeStatus({
      state: "CONNECTED",
      sessionId: instanceName,
      processId: process.pid,
      phoneJid: confirmedJid,
      phoneNumber: confirmedPhoneDigits ? `+${confirmedPhoneDigits}` : "",
      accountName: String(sock?.user?.name || ""),
      lastUpdatedAtUtc: new Date().toISOString(),
      error: ""
    });
  }

  if (connection === "close") {
    isSocketOpen = false;
    const statusCode = lastDisconnect?.error?.output?.statusCode;
    const loggedOut = statusCode === DisconnectReason.loggedOut;
    const errorMessage = serializeError(lastDisconnect?.error);

    await writeStatus({
      state: loggedOut ? "DISCONNECTED" : "RECONNECTING",
      sessionId: instanceName,
      processId: process.pid,
      error: errorMessage,
      lastUpdatedAtUtc: new Date().toISOString()
    });

    if (!loggedOut && !isStopping) {
      scheduleReconnect();
    }
  }
}

function scheduleReconnect() {
  if (isStopping || reconnectTimer) {
    return;
  }

  reconnectAttempt += 1;
  const delayMs = Math.min(15000, 2000 * reconnectAttempt);
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    void connectSocket();
  }, delayMs);
}

function clearReconnectTimer() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
}

async function readStatus() {
  try {
    const raw = await fsp.readFile(statusFile, "utf8");
    return JSON.parse(raw);
  } catch {
    return {};
  }
}

async function writeStatus(status) {
  const payload = JSON.stringify(status, null, 2);
  await fsp.writeFile(statusFile, payload, "utf8");
}

async function processOutbox() {
  if (isProcessingOutbox || !sock || !isSocketOpen) {
    return;
  }

  isProcessingOutbox = true;
  try {
    const files = (await fsp.readdir(outboxDir))
      .filter((file) => file.toLowerCase().endsWith(".json"))
      .sort((a, b) => a.localeCompare(b));

    for (const file of files) {
      const commandPath = path.join(outboxDir, file);
      let command;

      try {
        const raw = await fsp.readFile(commandPath, "utf8");
        command = JSON.parse(raw);
      } catch (error) {
        await writeCommandResult({
          id: path.parse(file).name,
          state: "ERROR",
          error: `No se pudo leer el comando: ${serializeError(error)}`
        });
        await safeUnlink(commandPath);
        continue;
      }

      const commandId = String(command?.id || path.parse(file).name);
      try {
        if (String(command?.type || "").toLowerCase() !== "send_text") {
          throw new Error(`Tipo de comando no soportado: ${command?.type || ""}`);
        }

        const jid = await resolveUserJid(command.phone);
        const content = { text: String(command.text || "") };
        const options = {};
        const replyToMessageId = String(command.replyToMessageId || "").trim();
        if (replyToMessageId) {
          options.quoted = { key: { remoteJid: jid, id: replyToMessageId, fromMe: false } };
        }

        const sent = await sock.sendMessage(jid, content, options);
        await writeCommandResult({
          id: commandId,
          state: "ENVIADO",
          externalMessageId: sent?.key?.id || "",
          destinationJid: jid
        });
      } catch (error) {
        await writeCommandResult({
          id: commandId,
          state: "ERROR_ENVIO",
          error: serializeError(error)
        });
      } finally {
        await safeUnlink(commandPath);
      }
    }
  } finally {
    isProcessingOutbox = false;
  }
}

async function writeCommandResult(result) {
  const resultId = String(result?.id || cryptoRandomId());
  const resultPath = path.join(resultsDir, `${resultId}.json`);
  await fsp.writeFile(resultPath, JSON.stringify(result, null, 2), "utf8");
}

async function handleMessagesUpsert(event) {
  const messages = Array.isArray(event?.messages) ? event.messages : [];
  for (const entry of messages) {
    try {
      const normalized = normalizeIncomingMessage(entry);
      if (!normalized) {
        continue;
      }

      const inboxFile = path.join(
        inboxDir,
        `${new Date().toISOString().replaceAll(":", "-")}-${normalized.messageId}.json`
      );

      await fsp.writeFile(inboxFile, JSON.stringify(normalized, null, 2), "utf8");
    } catch (error) {
      await writeStatus({
        ...(await readStatus()),
        state: "ERROR",
        sessionId: instanceName,
        processId: process.pid,
        error: `Inbox error: ${serializeError(error)}`,
        lastUpdatedAtUtc: new Date().toISOString()
      });
    }
  }
}

// proto.WebMessageInfo.Status real de esta versión de Baileys (7.0.0-rc14):
// ERROR=0, PENDING=1, SERVER_ACK=2, DELIVERY_ACK=3, READ=4, PLAYED=5.
// SERVER_ACK ya está cubierto por el "ENVIADO" que AlfaCore setea apenas sock.sendMessage resuelve
// -- solo interesan las transiciones posteriores (entregado/leído/error) para no reescribir un
// estado que ya está bien.
const ACK_STATUS_LABELS = {
  0: "ERROR",
  3: "DELIVERED",
  4: "READ",
  5: "READ"
};

/**
 * Los acks/receipts de mensajes salientes (fromMe) llegan por messages.update, separado de
 * messages.upsert -- nunca crean una conversación/mensaje nuevo, solo reportan el cambio de estado
 * de un mensaje que AlfaCore ya envió. Se identifican por key.id, el mismo id que
 * WhatsAppMessageId guarda en AlfaCore desde SendTextAsync.
 */
async function handleMessagesUpdate(updates) {
  const list = Array.isArray(updates) ? updates : [];
  for (const entry of list) {
    try {
      const externalMessageId = String(entry?.key?.id || "");
      const isFromMe = entry?.key?.fromMe === true;
      const status = entry?.update?.status;
      const label = ACK_STATUS_LABELS[status];
      if (!externalMessageId || !isFromMe || !label) {
        continue;
      }

      const ackFile = path.join(
        acksDir,
        `${new Date().toISOString().replaceAll(":", "-")}-${externalMessageId}.json`
      );

      await fsp.writeFile(
        ackFile,
        JSON.stringify(
          {
            externalMessageId,
            status: label,
            timestampUtc: new Date().toISOString()
          },
          null,
          2
        ),
        "utf8"
      );
    } catch (error) {
      await writeStatus({
        ...(await readStatus()),
        state: "ERROR",
        sessionId: instanceName,
        processId: process.pid,
        error: `Ack error: ${serializeError(error)}`,
        lastUpdatedAtUtc: new Date().toISOString()
      });
    }
  }
}

function normalizeIncomingMessage(entry) {
  const remoteJid = String(entry?.key?.remoteJid || "");
  if (!remoteJid || remoteJid.endsWith("@g.us")) {
    return null;
  }

  // Estados de WhatsApp (remoteJid = "status@broadcast") y canales/newsletters llegan por el mismo
  // evento messages.upsert que los mensajes reales. Se descartan por remoteJid ANTES de mirar
  // remoteJidAlt: Baileys expone ahí el JID real de quien publicó el estado (para poder mostrar el
  // nombre), lo que hacía que el filtro de teléfono de más abajo lo dejara pasar como si fuera un
  // mensaje directo normal -- creando una conversación fantasma con el nombre del contacto.
  if (remoteJid.endsWith("@broadcast") || remoteJid.endsWith("@newsletter")) {
    return null;
  }

  if (entry?.key?.fromMe) {
    return null;
  }

  // Mensajes de protocolo (revoke, cambios de ephemeral, edits, etc.) viajan sin contenido real
  // dentro de messages.upsert -- dejarlos pasar crearía una conversación/mensaje vacío por un
  // evento que no es una conversación, igual que el bug de Status.
  if (entry?.message?.protocolMessage) {
    return null;
  }

  // Baileys entrega "stubs" (sin objeto message real) para eventos que no tienen contenido
  // recuperable -- el caso confirmado en producción es messageStubType 2
  // ("Message absent from node": el cifrado nunca llegó, típico de LID/multi-dispositivo).
  // Sin esto, el mensaje se creaba igual con Texto vacío: aparecía nombre y hora en la burbuja
  // pero ningún texto, porque no hay nada real que extraer.
  if (!entry?.message) {
    return null;
  }

  // Con "addressingMode":"lid", remoteJid es un identificador de privacidad de WhatsApp, no el
  // teléfono real. Baileys expone el JID clásico (con el teléfono real) en remoteJidAlt cuando
  // el mensaje llegó direccionado por LID; si no existe, remoteJid ya es el JID con el teléfono.
  const phoneJid = String(entry?.key?.remoteJidAlt || remoteJid);
  const phoneValue = phoneJid.split("@")[0]?.replace(/\D/g, "") || "";
  if (!phoneValue) {
    return null;
  }

  const text =
    entry?.message?.conversation ||
    entry?.message?.extendedTextMessage?.text ||
    entry?.message?.imageMessage?.caption ||
    entry?.message?.videoMessage?.caption ||
    "";

  return {
    instanceName,
    phone: phoneValue.startsWith("+") ? phoneValue : `+${phoneValue}`,
    contactName: String(entry?.pushName || ""),
    messageId: String(entry?.key?.id || cryptoRandomId()),
    replyToMessageId: String(entry?.message?.extendedTextMessage?.contextInfo?.stanzaId || ""),
    messageType: resolveMessageType(entry),
    text: String(text || ""),
    timestampUtc: resolveTimestamp(entry),
    rawJson: JSON.stringify(entry)
  };
}

function resolveMessageType(entry) {
  if (entry?.message?.reactionMessage?.text) {
    return "REACTION";
  }

  return "TEXT";
}

function resolveTimestamp(entry) {
  const source = Number(entry?.messageTimestamp || 0);
  if (!Number.isFinite(source) || source <= 0) {
    return new Date().toISOString();
  }

  return new Date(source * 1000).toISOString();
}

function toUserJid(value) {
  const digits = String(value || "").replace(/\D/g, "");
  if (!digits) {
    throw new Error("Número de destino inválido para WhatsApp Web.");
  }

  return `${digits}@s.whatsapp.net`;
}

async function resolveUserJid(value) {
  const jid = toUserJid(value);
  const matches = await sock.onWhatsApp(jid);
  const match = Array.isArray(matches) ? matches.find((item) => item?.exists) : null;
  if (!match?.exists) {
    throw new Error(`El numero de destino no tiene WhatsApp o no se pudo resolver: ${jid}`);
  }

  return String(match.jid || jid);
}

async function safeUnlink(filePath) {
  try {
    await fsp.unlink(filePath);
  } catch {
    // Si el archivo ya no existe o quedó tomado un instante, seguimos igual.
  }
}

function cryptoRandomId() {
  return `${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
}

function serializeError(error) {
  if (!error) {
    return "";
  }

  if (typeof error === "string") {
    return error;
  }

  if (error instanceof Error) {
    return `${error.name}: ${error.message}`;
  }

  try {
    return JSON.stringify(error);
  } catch {
    return String(error);
  }
}
