// Type-ahead user search shared by the repo collaborators page and the group members page.
function initCollaboratorSearch(inputId, resultsId, searchUrl) {
	var input = document.getElementById(inputId);
	var results = document.getElementById(resultsId);
	if (!input || !results) return;
	var debounceTimer = null;
	var currentRequest = null;

	function hideResults() {
		results.innerHTML = '';
		results.classList.remove('open');
	}

	function renderResults(users) {
		if (!users.length) { hideResults(); return; }
		results.innerHTML = '';
		users.forEach(function (u) {
			var item = document.createElement('div');
			item.className = 'collaborator-result-item';
			var label = u.userName + (u.displayName ? ' — ' + u.displayName : '') +
				(u.email ? ' (' + u.email + ')' : '');
			item.textContent = label;
			item.addEventListener('click', function () {
				input.value = u.userName;
				hideResults();
			});
			results.appendChild(item);
		});
		results.classList.add('open');
	}

	input.addEventListener('input', function () {
		var q = input.value.trim();
		clearTimeout(debounceTimer);
		if (q.length < 2) { hideResults(); return; }

		debounceTimer = setTimeout(function () {
			if (currentRequest) currentRequest.abort();
			var controller = new AbortController();
			currentRequest = controller;
			fetch(searchUrl + '?q=' + encodeURIComponent(q), { signal: controller.signal })
				.then(function (r) { return r.json(); })
				.then(renderResults)
				.catch(function () { });
		}, 200);
	});

	document.addEventListener('click', function (e) {
		if (e.target !== input && !results.contains(e.target)) hideResults();
	});
}
