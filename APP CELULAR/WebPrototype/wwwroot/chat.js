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
            for (const mensagem of mensagens)
                porId.set(chaveMensagemCache(mensagem), mensagem);
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
        const request = indexedDB.open("appCelularChat", 1);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains("conversas"))
                request.result.createObjectStore("conversas", { keyPath: "chave" });
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
