window.appCelular = window.appCelular || {};
window.appCelular.scrollMessagesToBottom = (element) => {
    if (element) element.scrollTop = element.scrollHeight;
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
