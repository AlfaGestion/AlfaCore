window.alfaCoreAuthTree = window.alfaCoreAuthTree || {
    setIndeterminate: function (element, value) {
        if (!element) {
            return;
        }

        element.indeterminate = !!value;
    }
};
