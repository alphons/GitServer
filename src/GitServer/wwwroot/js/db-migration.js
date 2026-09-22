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

	function setBusy(btn, busy, busyLabel) {
		if (busy) {
			btn.dataset.originalText = btn.textContent;
			btn.textContent = '';
			var spinner = document.createElement('span');
			spinner.className = 'btn-spinner';
			btn.appendChild(spinner);
			btn.appendChild(document.createTextNode(busyLabel));
		} else {
			btn.textContent = btn.dataset.originalText;
		}
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
		setBusy(testBtn, true, labels.testBusy);
		resultEl.textContent = '';
		post(apiUrl + '/test').then(function (check) {
			if (!check.canConnect) { showResult(check.error || labels.testFailed, true); return; }
			if (!check.isFresh) { showResult(labels.testNotFresh, true); return; }
			showResult(labels.testOk, false);
			runBtn.disabled = false;
		}).catch(function () {
			showResult(labels.testFailed, true);
		}).finally(function () {
			testBtn.disabled = false;
			setBusy(testBtn, false);
		});
	});

	runBtn.addEventListener('click', function () {
		gsConfirm(labels.runConfirm, labels.runButton, labels.cancel, function () {
			testBtn.disabled = true;
			runBtn.disabled = true;
			setBusy(runBtn, true, labels.runBusy);
			resultEl.textContent = '';
			post(apiUrl + '/run').then(function (result) {
				if (!result.success) { showResult(labels.runFailed + ': ' + result.error, true); return; }
				showResult(labels.runSuccess + ' ' + labels.runRestartHint, false);
				showTable(result.tables);
			}).catch(function () {
				showResult(labels.runFailed, true);
			}).finally(function () {
				testBtn.disabled = false;
				setBusy(runBtn, false);
			});
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
