// Generic confirm modal: replaces native confirm() everywhere. Any <form> with a
// data-confirm="message" attribute pops this modal on submit instead of submitting directly;
// data-confirm-ok / data-confirm-cancel override the button labels (defaults: OK / Cancel).
(function () {
	var backdrop = null;

	function ensureModal() {
		if (backdrop) return backdrop;
		backdrop = document.createElement('div');
		backdrop.className = 'gs-confirm-backdrop';
		backdrop.hidden = true;
		backdrop.innerHTML =
			'<div class="gs-confirm-modal" role="alertdialog" aria-modal="true">' +
				'<div class="gs-confirm-text"></div>' +
				'<div class="gs-confirm-actions">' +
					'<button type="button" class="btn btn-danger gs-confirm-ok"></button>' +
					'<button type="button" class="btn btn-ghost gs-confirm-cancel"></button>' +
				'</div>' +
			'</div>';
		document.body.appendChild(backdrop);
		return backdrop;
	}

	function showConfirm(message, okLabel, cancelLabel, onConfirm) {
		var el = ensureModal();
		el.querySelector('.gs-confirm-text').textContent = message;
		var okBtn = el.querySelector('.gs-confirm-ok');
		var cancelBtn = el.querySelector('.gs-confirm-cancel');
		okBtn.textContent = okLabel || 'OK';
		cancelBtn.textContent = cancelLabel || 'Cancel';

		function cleanup() {
			el.hidden = true;
			okBtn.removeEventListener('click', onOk);
			cancelBtn.removeEventListener('click', onCancel);
			el.removeEventListener('click', onBackdropClick);
			document.removeEventListener('keydown', onKeyDown);
		}
		function onOk() { cleanup(); onConfirm(); }
		function onCancel() { cleanup(); }
		function onBackdropClick(e) { if (e.target === el) cleanup(); }
		function onKeyDown(e) { if (e.key === 'Escape') cleanup(); }

		okBtn.addEventListener('click', onOk);
		cancelBtn.addEventListener('click', onCancel);
		el.addEventListener('click', onBackdropClick);
		document.addEventListener('keydown', onKeyDown);
		el.hidden = false;
		okBtn.focus();
	}

	window.gsConfirm = showConfirm;

	document.addEventListener('submit', function (e) {
		var form = e.target;
		if (!(form instanceof HTMLFormElement)) return;
		var message = form.getAttribute('data-confirm');
		if (!message || form.dataset.gsConfirmed === 'true') return;

		e.preventDefault();
		showConfirm(message, form.dataset.confirmOk, form.dataset.confirmCancel, function () {
			form.dataset.gsConfirmed = 'true';
			form.submit();
		});
	});
})();

// Mobile nav toggle
(function () {
	const toggle = document.getElementById('navToggle');
	const menu = document.getElementById('navMenu');

	if (toggle && menu) {
		toggle.addEventListener('click', function () {
			const open = menu.classList.toggle('open');
			toggle.setAttribute('aria-expanded', open);
		});

		// Close on outside click
		document.addEventListener('click', function (e) {
			if (!toggle.contains(e.target) && !menu.contains(e.target)) {
				menu.classList.remove('open');
				toggle.setAttribute('aria-expanded', 'false');
			}
		});
	}
})();
