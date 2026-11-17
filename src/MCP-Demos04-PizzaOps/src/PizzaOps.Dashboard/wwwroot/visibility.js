export async function observeVisibility(receiver) {
    let disposed = false;
    let pending = false;
    let sending = false;

    async function publish() {
        pending = true;
        if (sending) return;
        sending = true;
        try {
            while (pending && !disposed) {
                pending = false;
                await receiver.invokeMethodAsync("VisibilityChanged", !document.hidden);
            }
        } catch (error) {
            // A disconnected circuit is visibly reported by Blazor's reconnect UI.
            console.warn("PizzaOps visibility notification failed; waiting for reconnection.");
        } finally {
            sending = false;
        }
    }

    const reconnect = document.getElementById("components-reconnect-modal");
    const recovered = event => {
        if (event.detail.state === "hide") void publish();
    };
    document.addEventListener("visibilitychange", publish);
    reconnect.addEventListener("components-reconnect-state-changed", recovered);
    await publish();
    return {
        dispose() {
            disposed = true;
            document.removeEventListener("visibilitychange", publish);
            reconnect.removeEventListener("components-reconnect-state-changed", recovered);
        }
    };
}
