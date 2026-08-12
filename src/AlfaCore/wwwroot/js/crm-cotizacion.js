window.crmCotizacion = {
    setHtml: function (el, html) {
        if (el) el.innerHTML = html || '';
    },
    getHtml: function (el) {
        return el ? el.innerHTML : '';
    },
    exec: function (command) {
        document.execCommand(command, false, null);
    },
    insertText: function (text) {
        document.execCommand('insertText', false, text);
    },
    focus: function (el) {
        if (el) el.focus();
    },
    copy: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(function () { });
        } else {
            var ta = document.createElement('textarea');
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); } catch (e) { }
            document.body.removeChild(ta);
        }
    },
    insertImage: function (el) {
        if (!el) return;
        var input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/*';
        input.onchange = function () {
            var file = input.files && input.files[0];
            if (!file) return;
            var reader = new FileReader();
            reader.onload = function () {
                el.focus();
                var img = '<img src="' + reader.result + '" style="max-width:100%;height:auto;" />';
                document.execCommand('insertHTML', false, img);
            };
            reader.readAsDataURL(file);
        };
        input.click();
    }
};
