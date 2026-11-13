// ============================================================================
// Velvet Rose — utilidades del chat (componente ChatBot)
// Funciones mínimas, sin dependencias: funcionan en Blazor Web y en el
// WebView de MAUI.
// ============================================================================
window.velvetChat = {
    /// Desplaza el contenedor de mensajes hasta el último mensaje.
    scrollToBottom: function (element) {
        if (!element) { return; }
        try {
            element.scrollTop = element.scrollHeight;
        } catch (e) {
            // Navegador sin scrollTop (no debería ocurrir): se ignora.
        }
    },

    /// Pone el foco en el campo de escritura del chat.
    focusInput: function () {
        var input = document.getElementById('vr-chat-input');
        if (input && typeof input.focus === 'function') {
            try { input.focus(); } catch (e) { /* ignorado */ }
        }
    }
};
