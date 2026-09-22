document.addEventListener('DOMContentLoaded', function () {
	var apiUrl = '/api/admin/reserved-names';
	var labels = JSON.parse(document.getElementById('reserved-labels').textContent);
	var token = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]').value;
	var listEl = document.getElementById('reserved-list');
	var errorEl = document.getElementById('reserved-error');
	var addForm = document.getElementById('reserved-add-form');
	var newInput = document.getElementById('reserved-new');
	var editingId = null;

	function showError(message) {
		errorEl.hidden = !message;
		errorEl.textContent = message || '';
	}

	// Sends a change to the API; resolves with the parsed body, or shows the server's error message.
	function send(method, url, body) {
		showError('');
		return fetch(url, {
			method: method,
			headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
			body: body ? JSON.stringify(body) : undefined,
		}).then(function (r) {
			if (r.ok) return r.status === 204 ? null : r.json();
			return r.json().catch(function () { return {}; }).then(function (e) {
				showError(e.error || labels.failed);
				throw new Error('rejected');
			});
		});
	}

	function button(text, className, onClick) {
		var b = document.createElement('button');
		b.type = 'button';
		b.className = 'btn btn-sm ' + className;
		b.textContent = text;
		b.addEventListener('click', onClick);
		return b;
	}

	function row(cells) {
		var tr = document.createElement('tr');
		cells.forEach(function (c) {
			var td = document.createElement('td');
			td.appendChild(c);
			tr.appendChild(td);
		});
		return tr;
	}

	function code(text) {
		var c = document.createElement('code');
		c.textContent = text;
		return c;
	}

	function editRow(p) {
		var input = document.createElement('input');
		input.type = 'text';
		input.className = 'form-control';
		input.maxLength = 64;
		input.value = p.pattern;
		var actions = document.createElement('span');
		actions.appendChild(button(labels.save, 'btn-primary', function () {
			send('POST', apiUrl + '/' + p.id + '/update', { pattern: input.value })
				.then(function () { editingId = null; load(); })
				.catch(function () { });
		}));
		actions.appendChild(document.createTextNode(' '));
		actions.appendChild(button(labels.cancel, 'btn-ghost', function () { editingId = null; showError(''); load(); }));
		return row([input, actions]);
	}

	function render(data) {
		listEl.textContent = '';
		var table = document.createElement('table');
		table.className = 'data-table';
		var head = document.createElement('tr');
		[labels.pattern, ''].forEach(function (h) {
			var th = document.createElement('th');
			th.textContent = h;
			head.appendChild(th);
		});
		var thead = document.createElement('thead');
		thead.appendChild(head);
		table.appendChild(thead);

		var tbody = document.createElement('tbody');
		data.builtIn.forEach(function (name) {
			var badge = document.createElement('span');
			badge.className = 'badge badge-read';
			badge.textContent = labels.builtIn;
			tbody.appendChild(row([code(name), badge]));
		});
		data.patterns.forEach(function (p) {
			if (p.id === editingId) { tbody.appendChild(editRow(p)); return; }
			var actions = document.createElement('span');
			actions.appendChild(button(labels.edit, 'btn-ghost', function () { editingId = p.id; showError(''); render(data); }));
			actions.appendChild(document.createTextNode(' '));
			actions.appendChild(button(labels.delete, 'btn-danger', function () {
				send('POST', apiUrl + '/' + p.id + '/delete').then(load).catch(function () { });
			}));
			tbody.appendChild(row([code(p.pattern), actions]));
		});
		table.appendChild(tbody);
		listEl.appendChild(table);
	}

	function load() {
		fetch(apiUrl).then(function (r) { return r.json(); }).then(render);
	}

	addForm.addEventListener('submit', function (e) {
		e.preventDefault();
		send('POST', apiUrl, { pattern: newInput.value })
			.then(function () { newInput.value = ''; load(); })
			.catch(function () { });
	});

	load();
});
