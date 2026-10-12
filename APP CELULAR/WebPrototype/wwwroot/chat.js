window.appCelular = window.appCelular || {};
window.appCelular.scrollMessagesToBottom = (element) => {
    if (!element) return;
    const scroll = () => { element.scrollTop = element.scrollHeight; };
    scroll();
    requestAnimationFrame(() => {
        scroll();
        setTimeout(scroll, 100);
    });
};
window.appCelular.getSavedSession = () => {
    try {
        const saved = localStorage.getItem("chatcat-session");
        return saved ? JSON.parse(saved) : null;
    } catch { return null; }
};
window.appCelular.saveSession = (session) => {
    if (session?.token && session?.expiraEm)
        localStorage.setItem("chatcat-session", JSON.stringify(session));
};
window.appCelular.clearSavedSession = () => localStorage.removeItem("chatcat-session");

window.appCelular.getChatMessages = async (key) => {
    const db = await abrirBancoChat();
    return new Promise((resolve, reject) => {
        const request = db.transaction("conversas", "readonly").objectStore("conversas").get(key);
        request.onsuccess = () => resolve(request.result?.mensagens ?? []);
        request.onerror = () => reject(request.error);
    });
};

window.appCelular.getHiddenChatMessages = (key) => {
    try {
        const value = localStorage.getItem(`chatcat-hidden-messages:${key}`);
        return value ? JSON.parse(value) : [];
    } catch { return []; }
};

window.appCelular.saveHiddenChatMessages = (key, messageKeys) => {
    localStorage.setItem(`chatcat-hidden-messages:${key}`, JSON.stringify(messageKeys ?? []));
};

window.appCelular.previewInputFile = (inputId) => {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file) throw new Error("Selecione novamente o arquivo para enviar.");
    return URL.createObjectURL(file);
};

window.appCelular.cacheInputFile = async (inputId, mediaKey) => {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file) return;
    const db = await abrirBancoChat();
    await new Promise((resolve, reject) => {
        const transaction = db.transaction("midias", "readwrite");
        transaction.objectStore("midias").put({ chave: mediaKey, blob: file });
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error);
    });
};

window.appCelular.loadChatMedia = async (conversationKey, messageId, signedUrl) => {
    const db = await abrirBancoChat();
    const key = String(messageId);
    const cached = await new Promise((resolve, reject) => {
        const request = db.transaction("midias", "readonly").objectStore("midias").get(key);
        request.onsuccess = () => resolve(request.result?.blob ?? null);
        request.onerror = () => reject(request.error);
    });
    if (cached) return URL.createObjectURL(cached);
    if (!signedUrl) return null;
    const response = await fetch(signedUrl);
    if (!response.ok) throw new Error(`Não foi possível baixar a mídia (${response.status}).`);
    const blob = await response.blob();
    await new Promise((resolve, reject) => {
        const transaction = db.transaction("midias", "readwrite");
        transaction.objectStore("midias").put({ chave: key, blob });
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error);
    });
    return URL.createObjectURL(blob);
};

window.appCelular.saveChatMessages = async (key, mensagens) => {
    if (!Array.isArray(mensagens) || mensagens.length === 0) return;
    const db = await abrirBancoChat();
    return new Promise((resolve, reject) => {
        const transaction = db.transaction("conversas", "readwrite");
        const store = transaction.objectStore("conversas");
        const request = store.get(key);
        request.onsuccess = () => {
            const existentes = request.result?.mensagens ?? [];
            const porId = new Map(existentes.map((mensagem) => [chaveMensagemCache(mensagem), mensagem]));
            for (const mensagem of mensagens) {
                const copia = { ...mensagem, fotoUrl: null, FotoUrl: null };
                porId.set(chaveMensagemCache(copia), copia);
            }
            store.put({ chave: key, mensagens: [...porId.values()] });
        };
        transaction.oncomplete = () => resolve();
        transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error);
    });
};

function chaveMensagemCache(mensagem) {
    const cliente = mensagem.clienteMensagemId ?? mensagem.ClienteMensagemId;
    if (cliente && cliente !== "00000000-0000-0000-0000-000000000000")
        return `cliente:${cliente.toLowerCase()}`;
    return `id:${mensagem.id ?? mensagem.Id}`;
}

function abrirBancoChat() {
    if (!window.indexedDB) return Promise.reject(new Error("O armazenamento local não está disponível."));
    return new Promise((resolve, reject) => {
        const request = indexedDB.open("appCelularChat", 2);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains("conversas"))
                request.result.createObjectStore("conversas", { keyPath: "chave" });
            if (!request.result.objectStoreNames.contains("midias"))
                request.result.createObjectStore("midias", { keyPath: "chave" });
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

(() => {
    let audio;
    const chamarAtencaoLocal = () => {
        try { navigator.vibrate?.([120, 70, 120]); } catch { /* O Safari iOS não oferece vibrador web. */ }
        try {
            const Audio = window.AudioContext || window.webkitAudioContext;
            if (!Audio) return;
            audio ??= new Audio();
            audio.resume();
            const inicio = audio.currentTime;
            [0, 0.22, 0.44].forEach((atraso) => {
                const oscilador = audio.createOscillator();
                const volume = audio.createGain();
                oscilador.type = "sine";
                oscilador.frequency.value = 880;
                volume.gain.setValueAtTime(0.0001, inicio + atraso);
                volume.gain.exponentialRampToValueAtTime(0.16, inicio + atraso + 0.015);
                volume.gain.exponentialRampToValueAtTime(0.0001, inicio + atraso + 0.12);
                oscilador.connect(volume);
                volume.connect(audio.destination);
                oscilador.start(inicio + atraso);
                oscilador.stop(inicio + atraso + 0.13);
            });
        } catch { /* O navegador pode bloquear áudio; o aviso visual continua funcionando. */ }
    };

    document.addEventListener("pointerdown", (event) => {
        if (event.target instanceof Element && event.target.closest("[data-chatcat-attention]"))
            chamarAtencaoLocal();
    }, true);
})();
