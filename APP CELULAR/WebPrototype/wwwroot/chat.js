window.appCelular = window.appCelular || {};
window.appCelular.scrollMessagesToBottom = (element) => {
    if (element) element.scrollTop = element.scrollHeight;
};
