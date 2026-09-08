// wwwroot/js/featureproposals.js
//
// Drives the "add idea" form on Pages/Features/Index.cshtml and the decision buttons on
// Pages/Features/Details.cshtml. Both post JSON to Endpoints/FeatureProposalEndpoints.cs, which
// (like this app's other minimal-API endpoints) needs no antiforgery token.
(function () {
    const errorBox = document.getElementById('feature-proposal-error');

    function showError(message) {
        if (!errorBox) { alert(message); return; }
        errorBox.textContent = message;
        errorBox.classList.remove('d-none');
    }

    const addForm = document.getElementById('add-idea-form');
    if (addForm) {
        addForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const title = document.getElementById('idea-title').value;
            const description = document.getElementById('idea-description').value;

            const response = await fetch('/api/feature-proposals', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ title, description })
            });

            if (response.ok) {
                window.location.reload();
            } else {
                showError(await response.text() || 'Could not submit that idea.');
            }
        });
    }

    document.querySelectorAll('[data-decide]').forEach((button) => {
        button.addEventListener('click', async () => {
            const decision = button.dataset.decide;
            const id = button.dataset.proposalId;
            let comment = null;

            if (decision === 'Revised') {
                const commentBox = document.getElementById('decision-comment');
                comment = commentBox ? commentBox.value.trim() : '';
                if (!comment) {
                    showError('Say what should change before sending this back for another round.');
                    return;
                }
            }

            const response = await fetch(`/api/feature-proposals/${id}/decide`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ decision, comment })
            });

            if (response.ok) {
                window.location.reload();
            } else {
                showError(await response.text() || 'Could not save that decision.');
            }
        });
    });
})();
