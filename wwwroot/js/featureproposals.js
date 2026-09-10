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

            const submitBtn = addForm.querySelector('button[type="submit"]');
            const originalText = submitBtn.innerHTML;
            submitBtn.disabled = true;
            submitBtn.classList.add('btn-clicked');
            submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Submitting…';

            const response = await fetch('/api/feature-proposals', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ title, description })
            });

            if (response.ok) {
                window.location.reload();
            } else {
                showError(await response.text() || 'Could not submit that idea.');
                submitBtn.disabled = false;
                submitBtn.classList.remove('btn-clicked');
                submitBtn.innerHTML = originalText;
            }
        });
    }

    // All four decision buttons share one in-flight request at a time, so a slow response can't
    // be raced by a second click on the same or a different button before the page reloads.
    const decideButtons = document.querySelectorAll('[data-decide]');
    decideButtons.forEach((button) => {
        button.addEventListener('click', async () => {
            const decision = button.dataset.decide;
            const id = button.dataset.proposalId;
            let comment = null;

            if (decision === 'Revised' || decision === 'TooComplex') {
                const commentBox = document.getElementById('decision-comment');
                comment = commentBox ? commentBox.value.trim() : '';
                if (decision === 'Revised' && !comment) {
                    showError('Say what should change before sending this back for another round.');
                    return;
                }
                if (!comment) comment = null;
            }

            // Answering a question is optional, same as the comment -- only checked radios are
            // sent. One radio group per question (name="question-<id>"), value is the option id.
            const answers = Array.from(document.querySelectorAll('input[type=radio][data-question-id]:checked'))
                .map((input) => ({
                    questionId: parseInt(input.dataset.questionId, 10),
                    selectedOptionId: parseInt(input.value, 10)
                }));

            const originalText = button.innerHTML;
            decideButtons.forEach((b) => { b.disabled = true; });
            button.classList.add('btn-clicked');
            button.innerHTML = `<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> ${originalText.trim()}`;

            const response = await fetch(`/api/feature-proposals/${id}/decide`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ decision, comment, answers })
            });

            if (response.ok) {
                window.location.reload();
            } else {
                showError(await response.text() || 'Could not save that decision.');
                decideButtons.forEach((b) => { b.disabled = false; });
                button.classList.remove('btn-clicked');
                button.innerHTML = originalText;
            }
        });
    });
})();
