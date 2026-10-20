'use strict';
(function () {
	// Resolves a relative markdown link/image path against the repo directory the
	// rendered file lives in, so "README.nl.md" from a root README resolves to the
	// repo root, not to whatever URL the browser happened to be on.
	function resolveRepoPath(baseDir, rel) {
		var segments = baseDir ? baseDir.split('/') : [];
		rel.split('/').forEach(function (part) {
			if (part === '' || part === '.') return;
			if (part === '..') segments.pop();
			else segments.push(part);
		});
		return segments.join('/');
	}

	function isRelative(href) {
		return !!href && !/^([a-z][a-z0-9+.-]*:|#|\/\/|\/)/i.test(href);
	}

	function rewriteRelativeUrls(el, baseDir, linkBase, imageBase, mdRootBase, mdBranch) {
		if (!linkBase && !imageBase) return;

		function rewrite(attrEl, attr, urlBase, allowMdRoot) {
			var href = attrEl.getAttribute(attr);
			if (!isRelative(href)) return;
			var hash = '';
			var hashIdx = href.indexOf('#');
			if (hashIdx >= 0) { hash = href.slice(hashIdx); href = href.slice(0, hashIdx); }
			if (!href) return;
			var resolved = resolveRepoPath(baseDir, href);

			// A link to a markdown file that lives directly under the repo root (no further
			// subfolder) goes to its own rendered viewer, not the generic blob/code view.
			if (allowMdRoot && mdRootBase && resolved.indexOf('/') === -1 && /\.md$/i.test(resolved)) {
				var branchQuery = mdBranch ? ('?branch=' + encodeURIComponent(mdBranch)) : '';
				attrEl.setAttribute(attr, mdRootBase + resolved + branchQuery + hash);
				return;
			}

			attrEl.setAttribute(attr, urlBase + resolved + hash);
		}

		if (linkBase) el.querySelectorAll('a[href]').forEach(function (a) { rewrite(a, 'href', linkBase, true); });
		if (imageBase) el.querySelectorAll('img[src]').forEach(function (img) { rewrite(img, 'src', imageBase, false); });
	}

	async function streamInto(el, res) {
		var streamer = new MarkdownStreamer(el);

		if (res.body && res.body.getReader) {
			var reader = res.body.getReader();
			var decoder = new TextDecoder();
			for (;;) {
				var chunk = await reader.read();
				if (chunk.done) break;
				streamer.markdown(decoder.decode(chunk.value, { stream: true }));
			}
			streamer.markdown(decoder.decode());
		} else {
			// No streaming body support (very old browser) — fall back to buffering the whole response.
			streamer.markdown(await res.text());
		}

		// A trailing block (e.g. a raw HTML block) right at end-of-input can stay stuck in the
		// parser's pending buffer and never get flushed to the DOM. Feeding a couple of trailing
		// newlines guarantees something follows the last block so it flushes.
		streamer.markdown('\n\n');

		if (window.hljs) {
			el.querySelectorAll('pre code').forEach(function (block) { hljs.highlightElement(block); });
		}

		rewriteRelativeUrls(el, el.getAttribute('data-link-dir') || '', el.getAttribute('data-link-base'), el.getAttribute('data-image-base'),
			el.getAttribute('data-md-root-base'), el.getAttribute('data-md-branch'));
	}

	function renderFrom(el, src) {
		return fetch(src)
			.then(function (res) { return res.ok ? streamInto(el, res) : Promise.reject(res.status); });
	}

	function renderAll() {
		document.querySelectorAll('[data-markdown-src]').forEach(function (el) {
			var src = el.getAttribute('data-markdown-src');
			el.removeAttribute('data-markdown-src');
			renderFrom(el, src).catch(function () { /* source unavailable — leave the container empty */ });
		});
	}

	window.renderMarkdownFrom = renderFrom;

	if (document.readyState === 'loading') {
		document.addEventListener('DOMContentLoaded', renderAll);
	} else {
		renderAll();
	}
})();
