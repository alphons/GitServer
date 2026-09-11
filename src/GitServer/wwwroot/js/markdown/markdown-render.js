'use strict';
(function () {
	function renderAll() {
		document.querySelectorAll('[data-markdown-source]').forEach(function (el) {
			var source = el.getAttribute('data-markdown-source') || '';
			el.removeAttribute('data-markdown-source');
			new MarkdownStreamer(el).markdown(source);
		});
	}

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', renderAll);
	} else {
		renderAll();
	}
})();
