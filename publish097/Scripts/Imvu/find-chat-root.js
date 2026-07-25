function __imvuFindChatRoot() {
    function findInDoc(doc) {
        const cont = doc.querySelector('div.chat-stream2, [class*="chat-stream2"]');
        const inp = doc.querySelector('div.input-container, [class*="input-container"]');
        if (cont || inp) return { doc, cont: cont || doc.body, hasStream: !!cont, hasInput: !!inp };
        return null;
    }
    let r = findInDoc(document);
    if (r) return r;
    for (const frame of document.querySelectorAll('iframe')) {
        try {
            const fd = frame.contentDocument || frame.contentWindow?.document;
            if (!fd) continue;
            r = findInDoc(fd);
            if (r) return r;
        } catch (e) {}
    }
    return { doc: document, cont: document.body, hasStream: false, hasInput: false };
}
