window.jsonViewer = {
    initialize: function () {
        document.querySelectorAll('.json-toggle').forEach(el => {
            el.addEventListener('click', () => {
                const block = el.parentElement.querySelector('.json-block');
                if (!block) return;
                const expanded = el.textContent === "▼";
                el.textContent = expanded ? "▶" : "▼";
                block.style.display = expanded ? "none" : "block";
            });
        });
    }
};
