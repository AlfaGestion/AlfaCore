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
        if (submitted || !code || !session) return;
        submitted = true;
        cleanup();
        await dotnet.invokeMethodAsync("CompleteEmbeddedSignupAuthorization", code, options.state, session.wabaId || "", session.phoneNumberId || "");
        code = null;
    };
    const onMessage = async event => {
        if (!allowedOrigins.has(event.origin) || typeof event.data !== "string") return;
        let payload;
        try { payload = JSON.parse(event.data); } catch { return; }
        if (payload?.type !== "WA_EMBEDDED_SIGNUP") return;
        const eventName = String(payload.event || "").toUpperCase();
        const coexistence = options.onboardingMode === "businessAppCoexistence";
        const isExpectedFinish = coexistence
            ? eventName === "FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING"
            : eventName === "FINISH";
        if (isExpectedFinish) {
            session = { wabaId: String(payload.data?.waba_id || ""), phoneNumberId: String(payload.data?.phone_number_id || "") };
            await completeIfReady();
        } else if (eventName === "CANCEL") {
            submitted = true;
            cleanup();
            await dotnet.invokeMethodAsync("EmbeddedSignupCancelled");
        } else if (eventName === "ERROR") {
            submitted = true;
            cleanup();
            await dotnet.invokeMethodAsync("EmbeddedSignupFailed", "META_EMBEDDED_SIGNUP_EVENT_ERROR");
        }
    };

    async function handleFacebookLoginResponse(loginResponse) {
        const receivedCode = loginResponse?.authResponse?.code;
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
