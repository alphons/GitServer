'use strict';
(function () {
	function renderAll() {
		document.querySelectorAll('script.markdown-source').forEach(function (scriptEl) {
			var target = scriptEl.nextElementSibling;
			var source = JSON.parse(scriptEl.textContent);
			scriptEl.remove();
			if (target) new MarkdownStreamer(target).markdown(source);
		});
	}

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', renderAll);
	} else {
		renderAll();
	}
})();
