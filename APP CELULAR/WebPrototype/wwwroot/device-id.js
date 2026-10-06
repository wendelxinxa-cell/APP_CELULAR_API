window.appCelular = window.appCelular || {};
window.appCelular.getOrCreateInstallationId = function () {
    const key = "app-celular-web-installation-id";
    let id = window.localStorage.getItem(key);
    if (id) return id;

    if (window.crypto && typeof window.crypto.randomUUID === "function") {
        id = window.crypto.randomUUID();
    } else {
        id = "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0;
            const v = c === "x" ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    window.localStorage.setItem(key, id);
    return id;
};
