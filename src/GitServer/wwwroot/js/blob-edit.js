// In-browser file editor on the blob page: Edit / Preview (markdown rendering, or a diff against the original),
// indent and wrap options, and committing straight onto the branch. All server work goes through /api/repos/{user}/{repo}/files.
(function () {
	'use strict';

	const view = document.getElementById('fileView');
	const editBtn = document.getElementById('editBtn');
	if (!view || !editBtn) return;

	const $ = (id) => document.getElementById(id);
	const i18n = JSON.parse($('blobEditI18n').textContent);
	const api = view.dataset.api;
	const branch = view.dataset.branch;
	const path = view.dataset.path;
	const fileName = view.dataset.fileName;
	const isMarkdown = view.dataset.markdown === 'true';

	const fileContent = $('fileContent');
	const editor = $('fileEditor');
	const viewActions = $('viewActions');
	const editActions = $('editActions');
	const commitBtn = $('editCommitBtn');
	const tabEdit = $('edTabEdit');
	const tabPreview = $('edTabPreview');
	const options = $('edOptions');
	const indentMode = $('edIndentMode');
	const indentSize = $('edIndentSize');
	const wrapMode = $('edWrap');
	const errorBox = $('edError');
	const pane = $('edPane');
	const gutter = $('edGutter');
	const ta = $('edText');
	const preview = $('edPreview');
	const dialog = $('commitDialog');
	const dlgMessage = $('commitMessage');
	const dlgDescription = $('commitDescription');
	const dlgError = $('commitError');
	const dlgOk = $('commitOkBtn');

	let original = '';       // the text as loaded, with LF line endings
	let baseSha = '';        // the branch tip the edit started from
	let editing = false;
	let committing = false;
	let previewToken = 0;
	let lastLineCount = -1;
	let gutterFrame = 0;

	const t = (key, ...args) => (i18n[key] || key).replace(/\{(\d+)\}/g, (_, i) => args[i]);
	const isDirty = () => editing && ta.value !== original;

	function store(key, value) { try { localStorage.setItem('gs.edit.' + key, value); } catch (e) { /* storage unavailable */ } }
	function stored(key) { try { return localStorage.getItem('gs.edit.' + key); } catch (e) { return null; } }

	function showError(box, message) {
		box.textContent = message;
		box.hidden = !message;
	}

	function call(method, url, body) {
		const init = { method, credentials: 'same-origin', headers: {} };
		if (body) {
			init.headers['Content-Type'] = 'application/json';
			init.body = JSON.stringify(body);
		}
		if (method !== 'GET') {
			init.headers['RequestVerificationToken'] = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]').value;
		}
		return fetch(url, init).then((res) => res.json().catch(() => null).then((data) => {
			if (!res.ok) {
				const err = new Error((data && data.error) || i18n.file_edit_error_failed);
				err.status = res.status;
				throw err;
			}
			return data;
		}));
	}

	// ---- Line numbers -----------------------------------------------------------------------

	function wrapping() { return wrapMode.value === 'soft'; }

	function updateGutter() {
		const count = ta.value.split('\n').length;
		if (count !== lastLineCount) {
			let html = '';
			for (let i = 1; i <= count; i++) html += '<div>' + i + '</div>';
			gutter.innerHTML = html;
			lastLineCount = count;
		}
		measureWrap();
		syncScroll();
	}

	// With soft wrap a line can take several rows: measure each one in a hidden copy of the text so its number keeps the same height.
	function measureWrap() {
		const rows = gutter.children;
		if (!wrapping()) {
			for (let i = 0; i < rows.length; i++) rows[i].style.height = '';
			return;
		}
		const style = getComputedStyle(ta);
		const mirror = document.createElement('div');
		mirror.className = 'editor-text editor-mirror wrap';
		mirror.style.width = (ta.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)) + 'px';
		const lines = ta.value.split('\n');
		for (let i = 0; i < lines.length; i++) {
			const row = document.createElement('div');
			row.textContent = lines[i] === '' ? '​' : lines[i];
			mirror.appendChild(row);
		}
		pane.appendChild(mirror);
		for (let i = 0; i < rows.length && i < mirror.children.length; i++) rows[i].style.height = mirror.children[i].offsetHeight + 'px';
		pane.removeChild(mirror);
	}

	function syncScroll() {
		gutter.style.paddingBottom = (ta.offsetHeight - ta.clientHeight) + 'px';   // room for a horizontal scrollbar
		gutter.scrollTop = ta.scrollTop;
	}

	function scheduleGutter() {
		if (gutterFrame) return;
		gutterFrame = requestAnimationFrame(() => { gutterFrame = 0; updateGutter(); });
	}

	// ---- Indentation ------------------------------------------------------------------------

	function applyIndentOptions() {
		pane.style.setProperty('--tab-size', indentSize.value);   // how wide a tab character is drawn
	}

	function indentUnit() {
		return indentMode.value === 'tabs' ? '\t' : ' '.repeat(parseInt(indentSize.value, 10));
	}

	function detectIndent(text) {
		const tabs = (text.match(/^\t/gm) || []).length;
		const spaces = (text.match(/^ {2,}/gm) || []).length;
		indentMode.value = tabs > spaces ? 'tabs' : (spaces > 0 ? 'spaces' : 'tabs');
	}

	// Replaces a range through the browser's own editing command, so undo / redo keep working.
	function replaceRange(start, end, text) {
		ta.focus();
		ta.setSelectionRange(start, end);
		if (!document.execCommand('insertText', false, text)) {
			ta.setRangeText(text, start, end, 'end');
			ta.dispatchEvent(new Event('input', { bubbles: true }));
		}
	}

	function indentSelection(outdent) {
		const value = ta.value;
		const start = ta.selectionStart;
		const end = ta.selectionEnd;
		const unit = indentUnit();
		const multiline = value.slice(start, end).includes('\n');

		if (!outdent && !multiline) {
			replaceRange(start, end, unit);
			return;
		}

		const lineStart = value.lastIndexOf('\n', start - 1) + 1;
		const lines = value.slice(lineStart, end).split('\n');
		let firstRemoved = 0;
		const changed = lines.map((line, i) => {
			if (!outdent) return line === '' ? line : unit + line;
			const spaces = line.match(/^ */)[0].length;
			const remove = line.startsWith('\t') ? 1 : Math.min(spaces, parseInt(indentSize.value, 10));
			if (i === 0) firstRemoved = remove;
			return line.slice(remove);
		});
		const block = changed.join('\n');
		replaceRange(lineStart, end, block);
		const newStart = outdent ? Math.max(lineStart, start - firstRemoved) : start + (lines[0] === '' ? 0 : unit.length);
		ta.setSelectionRange(newStart, lineStart + block.length);
	}

	ta.addEventListener('keydown', (e) => {
		if (e.ctrlKey || e.metaKey || e.altKey) return;
		if (e.key === 'Tab') {
			e.preventDefault();
			indentSelection(e.shiftKey);
		} else if (e.key === 'Enter' && !e.shiftKey) {
			// keep the indentation of the line being split
			e.preventDefault();
			const lineStart = ta.value.lastIndexOf('\n', ta.selectionStart - 1) + 1;
			const lead = ta.value.slice(lineStart, ta.selectionStart).match(/^[ \t]*/)[0];
			replaceRange(ta.selectionStart, ta.selectionEnd, '\n' + lead);
		}
	});

	ta.addEventListener('input', () => {
		commitBtn.disabled = !isDirty();
		showError(errorBox, '');
		scheduleGutter();
	});
	ta.addEventListener('scroll', syncScroll);
	if (window.ResizeObserver) new ResizeObserver(() => { if (editing && wrapping()) scheduleGutter(); }).observe(ta);

	indentMode.addEventListener('change', () => { applyIndentOptions(); });
	indentSize.addEventListener('change', () => { store('indentSize', indentSize.value); applyIndentOptions(); });
	wrapMode.addEventListener('change', () => {
		store('wrap', wrapMode.value);
		ta.classList.toggle('wrap', wrapping());
		ta.setAttribute('wrap', wrapping() ? 'soft' : 'off');
		scheduleGutter();
	});

	// ---- Edit / Preview ---------------------------------------------------------------------

	function setMode(mode) {
		const previewing = mode === 'preview';
		tabEdit.classList.toggle('active', !previewing);
		tabPreview.classList.toggle('active', previewing);
		tabEdit.setAttribute('aria-selected', String(!previewing));
		tabPreview.setAttribute('aria-selected', String(previewing));
		pane.hidden = previewing;
		options.hidden = previewing;
		preview.hidden = !previewing;
		if (previewing) renderPreview(); else { previewToken++; ta.focus(); scheduleGutter(); }
	}

	function renderPreview() {
		showError(errorBox, '');
		preview.textContent = '';
		const token = ++previewToken;

		if (isMarkdown) {
			preview.className = 'editor-preview markdown-body pane-output';
			try {
				const streamer = new MarkdownStreamer(preview);
				streamer.markdown(ta.value);
				streamer.markdown('\n\n');   // flush a trailing block
				if (window.hljs) preview.querySelectorAll('pre code').forEach((b) => hljs.highlightElement(b));
			} catch (e) {
				showError(errorBox, i18n.file_edit_error_failed);
			}
			return;
		}

		preview.className = 'editor-preview';
		preview.textContent = t('file_edit_loading');
		call('POST', api + '/diff', { branch, path, content: ta.value, baseSha })
			.then((diff) => { if (token === previewToken) renderDiff(diff); })
			.catch((err) => { if (token === previewToken) { preview.textContent = ''; showError(errorBox, err.message); } });
	}

	function cell(tag, className, text) {
		const el = document.createElement(tag);
		el.className = className;
		if (text != null) el.textContent = text;
		return el;
	}

	// Side by side, like a split diff: deleted lines on the left, added lines on the right, changes paired up row by row.
	function renderDiff(diff) {
		preview.textContent = '';
		if (!diff.hunks.length) {
			preview.appendChild(cell('div', 'diff-empty', t('file_edit_no_changes')));
			return;
		}

		const table = cell('table', 'diff-table');
		const cols = document.createElement('colgroup');   // fixed layout takes widths from here, not from the full-width hunk header row
		['diff-ln', 'diff-code', 'diff-ln', 'diff-code'].forEach((c) => cols.appendChild(cell('col', c)));
		table.appendChild(cols);
		const body = document.createElement('tbody');
		table.appendChild(body);

		diff.hunks.forEach((hunk) => {
			const head = document.createElement('tr');
			const headCell = cell('td', 'diff-hunk', hunk.header);
			headCell.colSpan = 4;
			head.appendChild(headCell);
			body.appendChild(head);

			const lines = hunk.lines;
			for (let i = 0; i < lines.length;) {
				const line = lines[i];
				const row = document.createElement('tr');

				if (line.type === 'context') {
					row.append(cell('td', 'diff-ln', line.oldNo), cell('td', 'diff-code', line.text),
						cell('td', 'diff-ln', line.newNo), cell('td', 'diff-code', line.text));
					i++;
				} else if (line.type === 'note') {
					const note = cell('td', 'diff-note', line.text);
					note.colSpan = 4;
					row.appendChild(note);
					i++;
				} else {
					// a run of deletions followed by additions: pair them up
					const dels = [];
					const adds = [];
					while (i < lines.length && lines[i].type === 'del') dels.push(lines[i++]);
					while (i < lines.length && lines[i].type === 'add') adds.push(lines[i++]);
					for (let k = 0; k < Math.max(dels.length, adds.length); k++) {
						const r = k === 0 ? row : document.createElement('tr');
						const d = dels[k];
						const a = adds[k];
						r.append(
							cell('td', 'diff-ln' + (d ? ' del' : ' empty'), d ? d.oldNo : null),
							cell('td', 'diff-code' + (d ? ' del' : ' empty'), d ? d.text : null),
							cell('td', 'diff-ln' + (a ? ' add' : ' empty'), a ? a.newNo : null),
							cell('td', 'diff-code' + (a ? ' add' : ' empty'), a ? a.text : null));
						if (k > 0) body.appendChild(r);
					}
				}
				body.appendChild(row);
			}
		});
		preview.appendChild(table);
	}

	tabEdit.addEventListener('click', () => setMode('edit'));
	tabPreview.addEventListener('click', () => setMode('preview'));

	// ---- Entering and leaving edit mode -----------------------------------------------------

	function enterEdit() {
		editing = true;
		fileContent.hidden = true;
		viewActions.hidden = true;
		editActions.hidden = false;
		editor.hidden = false;
		commitBtn.disabled = true;
		setMode('edit');
		updateGutter();
		ta.focus();
		ta.setSelectionRange(0, 0);
	}

	function exitEdit() {
		editing = false;
		previewToken++;
		editor.hidden = true;
		editActions.hidden = true;
		fileContent.hidden = false;
		viewActions.hidden = false;
		showError(errorBox, '');
		ta.value = '';
		lastLineCount = -1;
	}

	editBtn.addEventListener('click', () => {
		editBtn.disabled = true;
		call('GET', api + '?branch=' + encodeURIComponent(branch) + '&path=' + encodeURIComponent(path))
			.then((file) => {
				baseSha = file.commitSha;
				original = file.content.replace(/\r\n/g, '\n');
				ta.value = original;
				detectIndent(original);
				indentSize.value = stored('indentSize') || '4';
				wrapMode.value = stored('wrap') === 'soft' ? 'soft' : 'off';
				wrapMode.dispatchEvent(new Event('change'));
				applyIndentOptions();
				enterEdit();
			})
			.catch((err) => alert(err.message))
			.finally(() => { editBtn.disabled = false; });
	});

	$('editCancelBtn').addEventListener('click', () => {
		if (!isDirty()) { exitEdit(); return; }
		gsConfirm(t('file_edit_discard_confirm'), t('file_edit_discard'), t('file_edit_keep'), exitEdit);
	});

	window.addEventListener('beforeunload', (e) => {
		if (isDirty() && !committing) { e.preventDefault(); e.returnValue = ''; }
	});

	// ---- Committing -------------------------------------------------------------------------

	function closeDialog() {
		dialog.hidden = true;
		document.removeEventListener('keydown', onDialogKey);
	}

	function onDialogKey(e) {
		if (e.key === 'Escape' && !committing) closeDialog();
	}

	commitBtn.addEventListener('click', () => {
		dlgMessage.value = t('file_edit_default_message', fileName);
		dlgDescription.value = '';
		$('commitBranchInfo').textContent = t('file_edit_dlg_branch', branch);
		showError(dlgError, '');
		dialog.hidden = false;
		document.addEventListener('keydown', onDialogKey);
		dlgMessage.focus();
		dlgMessage.select();
	});

	$('commitCancelBtn').addEventListener('click', () => { if (!committing) closeDialog(); });
	dialog.addEventListener('click', (e) => { if (e.target === dialog && !committing) closeDialog(); });
	dlgMessage.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); dlgOk.click(); } });

	dlgOk.addEventListener('click', () => {
		if (committing) return;
		if (!dlgMessage.value.trim()) { dlgMessage.focus(); return; }

		committing = true;
		dlgOk.disabled = true;
		showError(dlgError, '');
		const label = dlgOk.textContent;
		dlgOk.innerHTML = '<span class="btn-spinner"></span>';
		dlgOk.appendChild(document.createTextNode(label));

		call('PUT', api, {
			branch, path, content: ta.value, baseSha,
			message: dlgMessage.value.trim(), description: dlgDescription.value.trim() || null,
		}).then((result) => {
			window.location.href = result.href;   // reload the page: the file now shows the new version
		}).catch((err) => {
			committing = false;
			dlgOk.disabled = false;
			dlgOk.textContent = label;
			showError(dlgError, err.message);
		});
	});
})();
