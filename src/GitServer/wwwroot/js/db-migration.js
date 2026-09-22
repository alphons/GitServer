document.addEventListener('DOMContentLoaded', function () {
	var apiUrl = '/api/admin/database-migration';
	var labels = JSON.parse(document.getElementById('dbmigration-labels').textContent);
	var token = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]').value;
	var unavailableEl = document.getElementById('dbmigration-unavailable');
	var actionsEl = document.getElementById('dbmigration-actions');
	var resultEl = document.getElementById('dbmigration-result');
	var testBtn = document.getElementById('dbmigration-test-btn');
	var runBtn = document.getElementById('dbmigration-run-btn');

	function post(url) {
		return fetch(url, { method: 'POST', headers: { 'RequestVerificationToken': token } }).then(function (r) { return r.json(); });
	}

	function showResult(message, isError) {
		resultEl.textContent = '';
		var p = document.createElement('p');
		p.className = isError ? 'alert alert-danger' : 'alert alert-success';
		p.textContent = message;
		resultEl.appendChild(p);
	}

	function showTable(rows) {
		var table = document.createElement('table');
		table.className = 'data-table';
		var head = document.createElement('tr');
		[labels.colTable, labels.colRows].forEach(function (h) {
			var th = document.createElement('th');
			th.textContent = h;
			head.appendChild(th);
		});
		var thead = document.createElement('thead');
		thead.appendChild(head);
		table.appendChild(thead);

		var tbody = document.createElement('tbody');
		rows.forEach(function (t) {
			var tr = document.createElement('tr');
			var tdTable = document.createElement('td');
			tdTable.textContent = t.table;
			var tdRows = document.createElement('td');
			tdRows.textContent = t.rows;
			tr.appendChild(tdTable);
			tr.appendChild(tdRows);
			tbody.appendChild(tr);
		});
		table.appendChild(tbody);
		resultEl.appendChild(table);
	}

	testBtn.addEventListener('click', function () {
		testBtn.disabled = true;
		runBtn.disabled = true;
		resultEl.textContent = '';
		post(apiUrl + '/test').then(function (check) {
			testBtn.disabled = false;
			if (!check.canConnect) { showResult(check.error || labels.testFailed, true); return; }
			if (!check.isFresh) { showResult(labels.testNotFresh, true); return; }
			showResult(labels.testOk, false);
			runBtn.disabled = false;
		});
	});

	runBtn.addEventListener('click', function () {
		if (!confirm(labels.runConfirm)) return;
		testBtn.disabled = true;
		runBtn.disabled = true;
		resultEl.textContent = '';
		post(apiUrl + '/run').then(function (result) {
			if (!result.success) { showResult(labels.runFailed + ': ' + result.error, true); return; }
			showResult(labels.runSuccess + ' ' + labels.runRestartHint, false);
			showTable(result.tables);
		});
	});

	fetch(apiUrl + '/status').then(function (r) { return r.json(); }).then(function (status) {
		if (!status.isAvailable) {
			unavailableEl.hidden = false;
			unavailableEl.textContent = labels.unavailableProvider;
			return;
		}
		if (!status.sqlServerConfigured) {
			unavailableEl.hidden = false;
			unavailableEl.textContent = labels.unavailableConnectionString;
			return;
		}
		actionsEl.hidden = false;
	});
});
