const allowedOrigins = new Set([
    "https://www.facebook.com",
    "https://web.facebook.com"
]);

export const MODULE_VERSION = "es2-coexistence-contract-1";

let sdkPromise;

function loadSdk(appId, graphApiVersion) {
    if (sdkPromise) return sdkPromise;
    sdkPromise = new Promise((resolve, reject) => {
        window.fbAsyncInit = () => {
            window.FB.init({ appId, autoLogAppEvents: true, xfbml: false, version: graphApiVersion });
            captureActivation("fb-init-complete", true);
            resolve();
        };
        const existing = document.getElementById("facebook-jssdk");
        if (existing) return;
        const script = document.createElement("script");
        script.id = "facebook-jssdk";
        script.async = true;
        script.defer = true;
        script.crossOrigin = "anonymous";
        script.src = "https://connect.facebook.net/es_LA/sdk.js";
        script.onerror = () => reject(new Error("No se pudo cargar el SDK oficial de Meta."));
        document.head.appendChild(script);
    });
    return sdkPromise;
}

function captureActivation(stage, enabled) {
    if (!enabled) return undefined;
    return window.alfaEs2Diagnostics?.capture(stage);
}

// Diagnóstico TEMPORAL de producción para el incidente Base4264 (2026-09-11, ver auditoría de
// onMessage/FB.login). Sólo booleanos/strings ya sanitizados por el propio llamador — nunca
// payload.data completo, waba_id, phone_number_id, code, state ni token. Prefijo [WAES-DIAG] para
// poder identificarlo y quitarlo fácilmente una vez resuelto el diagnóstico.
function diagLog(stage, details) {
    try {
        console.debug("[WAES-DIAG]", stage, details);
    } catch {
        // Un logger nunca debe romper el flujo de autorización real.
    }
}

function captureLoginContract(contract, enabled) {
    if (!enabled) return;
    const metadata = Object.freeze({ ...contract });
    if (window.alfaEs2Diagnostics)
        window.alfaEs2Diagnostics.loginContract = metadata;
    console.debug("[ES2] login-contract", metadata);
}

function findFunctionPaths(value, path = "options", seen = new WeakSet()) {
    if (typeof value === "function")
        return [{ path, constructorName: value.constructor?.name || "unknown" }];
    if (value === null || typeof value !== "object" || seen.has(value)) return [];
    seen.add(value);
    return Object.entries(value).flatMap(([key, child]) => findFunctionPaths(child, `${path}.${key}`, seen));
}

function sanitizeStack(stack) {
    return String(stack || "")
        .replace(/([?&](?:code|state|access_token|token)=)[^&\s)]+/gi, "$1<redacted>")
        .slice(0, 4000);
}

function createFacebookLoginError(error, callbackType, functionPaths, loginContract = undefined) {
    const details = {
        moduleVersion: MODULE_VERSION,
        errorName: String(error?.name || "Error"),
        errorMessage: String(error?.message || error || "FB.login falló."),
        stack: sanitizeStack(error?.stack),
        callbackType,
        optionFunctionPaths: functionPaths,
        loginContract,
        userActivation: window.alfaEs2Diagnostics?.activation || {}
    };
    return new Error(`FB.login diagnostic: ${JSON.stringify(details)}`);
}

captureActivation("module-loaded", true);

export async function launch(options, dotnet) {
    captureActivation("sdk-requested", options.developmentDiagnostics);
    await loadSdk(options.appId, options.graphApiVersion);
    captureActivation("sdk-loaded", options.developmentDiagnostics);
    let code = null;
    let session = null;
    let submitted = false;

    const cleanup = () => window.removeEventListener("message", onMessage);
    const completeIfReady = async () => {
        const hasCode = !!code;
        const hasSession = !!session;
        diagLog("completeIfReady", { completeIfReadyCalled: true, hasCode, hasSession });
        if (submitted || !code || !session) return;
        submitted = true;
        cleanup();
        try {
            await dotnet.invokeMethodAsync("CompleteEmbeddedSignupAuthorization", code, options.state, session.wabaId || "", session.phoneNumberId || "");
            diagLog("completeIfReady", { dotnetCallbackInvoked: true });
        } catch (error) {
            diagLog("completeIfReady", {
                dotnetCallbackInvoked: false,
                dotnetCallbackError: true,
                errorType: String(error?.name || "Error"),
                errorMessage: sanitizeStack(String(error?.message || error || ""))
            });
            throw error;
        }
        code = null;
    };
    const onMessage = async event => {
        const originAllowed = allowedOrigins.has(event.origin);
        const dataType = typeof event.data;
        let jsonParseOk = false;
        let payload;
        if (originAllowed && dataType === "string") {
            try { payload = JSON.parse(event.data); jsonParseOk = true; } catch { jsonParseOk = false; }
        }
        const payloadType = payload && typeof payload === "object" && typeof payload.type === "string" ? payload.type : null;
        const payloadEvent = payload && typeof payload === "object" && typeof payload.event === "string" ? payload.event : null;
        const eventName = String(payloadEvent || "").toUpperCase();
        const coexistence = options.onboardingMode === "businessAppCoexistence";
        // Meta no siempre manda FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING en modo coexistence: el
        // intento real de Base4264/Alfanet Papelera llegó con eventName="FINISH" incluso en
        // coexistence (confirmado por [WAES-DIAG]). Aceptamos FINISH siempre, y además el evento
        // específico de coexistence por si Meta lo manda en otros casos/versiones. No relaja nada de
        // seguridad: sigue exigiendo payload.type/origin/parse válidos (ver isKnownPayload) y
        // completeIfReady sigue exigiendo code Y session antes de invocar a .NET.
        const isExpectedFinish =
            eventName === "FINISH" ||
            (coexistence && eventName === "FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING");
        const isCancel = eventName === "CANCEL";
        const isError = eventName === "ERROR";
        const isKnownPayload = originAllowed && dataType === "string" && jsonParseOk && payloadType === "WA_EMBEDDED_SIGNUP";
        const eventAccepted = isKnownPayload && (isExpectedFinish || isCancel || isError);
        const expectedEvents = coexistence ? ["FINISH", "FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING"] : ["FINISH"];
        diagLog("onMessage", {
            postMessageReceived: true,
            origin: event.origin,
            dataType,
            jsonParseOk,
            payloadType,
            payloadEvent,
            expectedEvents,
            eventAccepted
        });

        if (!isKnownPayload) return;
        if (isExpectedFinish) {
            session = { wabaId: String(payload.data?.waba_id || ""), phoneNumberId: String(payload.data?.phone_number_id || "") };
            diagLog("onMessage", { sessionExtracted: true, hasWabaId: !!session.wabaId, hasPhoneNumberId: !!session.phoneNumberId });
            await completeIfReady();
        } else if (isCancel) {
            submitted = true;
            cleanup();
            await dotnet.invokeMethodAsync("EmbeddedSignupCancelled");
        } else if (isError) {
            submitted = true;
            cleanup();
            await dotnet.invokeMethodAsync("EmbeddedSignupFailed", "META_EMBEDDED_SIGNUP_EVENT_ERROR");
        }
    };

    async function handleFacebookLoginResponse(loginResponse) {
        const hasAuthResponse = !!loginResponse?.authResponse;
        const receivedCode = loginResponse?.authResponse?.code;
        const hasCode = !!receivedCode;
        diagLog("facebookLoginCallback", { facebookLoginCallbackInvoked: true, hasAuthResponse, hasCode });
        if (!receivedCode) {
            if (!submitted) {
                submitted = true;
                cleanup();
                await dotnet.invokeMethodAsync("EmbeddedSignupCancelled");
            }
            return;
        }
        code = String(receivedCode);
        await completeIfReady();
    }

    function facebookLoginCallback(loginResponse) {
        void handleFacebookLoginResponse(loginResponse);
    }

    const configId = typeof options.config_id === "string" ? options.config_id.trim() : "";
    const coexistence = options.onboardingMode === "businessAppCoexistence";
    const loginOptions = {
        config_id: configId,
        response_type: "code",
        override_default_response_type: true,
        extras: coexistence
            ? { setup: {}, featureType: "whatsapp_business_app_onboarding", sessionInfoVersion: "3" }
            : { sessionInfoVersion: "3" }
    };
    const setupPresent = Object.prototype.hasOwnProperty.call(loginOptions.extras, "setup");
    const setupIsObject = setupPresent
        && loginOptions.extras.setup !== null
        && typeof loginOptions.extras.setup === "object"
        && !Array.isArray(loginOptions.extras.setup);
    const featureTypePresent = Object.prototype.hasOwnProperty.call(loginOptions.extras, "featureType");
    const loginContract = {
        moduleVersion: MODULE_VERSION,
        onboardingMode: coexistence ? "businessAppCoexistence" : "standard",
        configIdPresent: typeof loginOptions.config_id === "string" && loginOptions.config_id.length > 0,
        setupPresent,
        featureTypePresent,
        featureType: featureTypePresent ? loginOptions.extras.featureType : undefined,
        sessionInfoVersion: loginOptions.extras.sessionInfoVersion
    };
    const loginContractValid = loginContract.configIdPresent
        && loginOptions.response_type === "code"
        && loginOptions.override_default_response_type === true
        && loginContract.sessionInfoVersion === "3"
        && (coexistence
            ? setupIsObject && loginContract.featureType === "whatsapp_business_app_onboarding"
            : !setupPresent && !featureTypePresent);
    captureLoginContract(loginContract, options.developmentDiagnostics);
    const callbackType = {
        typeof: typeof facebookLoginCallback,
        constructorIsFunction: facebookLoginCallback.constructor === Function,
        constructorName: facebookLoginCallback.constructor?.name || "unknown",
        objectTag: Object.prototype.toString.call(facebookLoginCallback)
    };
    const functionPaths = findFunctionPaths(loginOptions);
    if (callbackType.typeof !== "function"
        || !callbackType.constructorIsFunction
        || callbackType.constructorName !== "Function"
        || callbackType.objectTag !== "[object Function]"
        || functionPaths.length > 0
        || !loginContractValid) {
        throw createFacebookLoginError(new Error("Validación local del contrato FB.login fallida."), callbackType, functionPaths, loginContract);
    }

    window.addEventListener("message", onMessage);
    captureActivation("before-fb-login", options.developmentDiagnostics);
    try {
        window.FB.login(facebookLoginCallback, loginOptions);
    } catch (error) {
        cleanup();
        throw createFacebookLoginError(error, callbackType, functionPaths, loginContract);
    }
}
