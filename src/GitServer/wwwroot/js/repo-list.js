// Client-side rendering of the repository lists that the server returns as JSON.
// All text goes through textContent, so repository data is never interpreted as HTML.
window.RepoList = (function () {
	function el(tag, className, text) {
		var node = document.createElement(tag);
		if (className) node.className = className;
		if (text != null) node.textContent = text;
		return node;
	}

	function badge(className, text) {
		return el('span', 'badge ' + className, text);
	}

	function pager(page, hasNext, labels, onPage) {
		if (page <= 0 && !hasNext) return null;
		var box = el('div', 'pagination');
		if (page > 0) {
			var prev = el('button', 'btn btn-secondary', labels.prev);
			prev.type = 'button';
			prev.addEventListener('click', function () { onPage(page - 1); });
			box.appendChild(prev);
		}
		if (hasNext) {
			var next = el('button', 'btn btn-secondary', labels.next);
			next.type = 'button';
			next.addEventListener('click', function () { onPage(page + 1); });
			box.appendChild(next);
		}
		return box;
	}

	function card(repo, labels) {
		var box = el('div', 'repo-card');

		var header = el('div', 'repo-card-header');
		var name = el('a', 'repo-name', repo.displayName);
		name.href = repo.href;
		header.appendChild(name);
		header.appendChild(repo.isPrivate ? badge('badge-warning', labels.private) : badge('badge-success', labels.public));
		if (repo.isReadOnly) header.appendChild(badge('badge-warning', labels.readOnly));
		box.appendChild(header);

		if (repo.description) box.appendChild(el('p', 'repo-description', repo.description));

		if (repo.collaborators && repo.collaborators.length) {
			var collab = el('p', 'text-muted repo-collaborators', labels.collaborators + ' ');
			repo.collaborators.forEach(function (c, i) {
				if (c.userName) {
					var a = el('a', null, c.userName);
					a.href = '/dashboard/User/' + encodeURIComponent(c.userName);
					collab.appendChild(a);
				} else {
					collab.appendChild(el('span', null, c.groupName));
				}
				if (i < repo.collaborators.length - 1) collab.appendChild(document.createTextNode(', '));
			});
			box.appendChild(collab);
		}

		if (repo.updated || repo.created) {
			var meta = el('div', 'repo-meta');
			meta.appendChild(el('span', 'text-muted', labels.updated + ' ' + repo.updated));
			meta.appendChild(el('span', 'text-muted', labels.created + ' ' + repo.created));
			box.appendChild(meta);
		}
		return box;
	}

	// Appends [pager, card list, pager] to parent. emptyNode is shown when there are no repos.
	function list(parent, repos, page, hasNext, labels, onPage, emptyNode) {
		if (!repos.length) {
			parent.appendChild(emptyNode);
			return;
		}
		var top = pager(page, hasNext, labels, onPage);
		if (top) parent.appendChild(top);
		var wrap = el('div', 'repo-list');
		repos.forEach(function (r) { wrap.appendChild(card(r, labels)); });
		parent.appendChild(wrap);
		var bottom = pager(page, hasNext, labels, onPage);
		if (bottom) parent.appendChild(bottom);
	}

	return { el: el, list: list };
})();
