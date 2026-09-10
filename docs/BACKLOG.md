# Backlog

Ideas for the automated feature/improvement loops (`docs/OPERATIONS.md` → "Automated daily loops")
to work from, one line each, roughly in priority order. A loop that finds an unaddressed item here
picks the top one; when this list is empty (as it is right now) or exhausted, the loop uses its own
judgment about what would genuinely help this app's actual users instead.

Add an item by just adding a line. Remove a line once a loop has shipped it (the loop itself won't
delete it -- that's a deliberate human checkpoint, not an oversight).

Lines starting `Build: ... (proposal #<id>): ...` are written automatically by
`.github/workflows/feature-review.yml` once a submission at `/Features` gets final approval (see
`CLAUDE.md` → "Feature proposals & review") -- treat those the same as a hand-added line, just
don't hand-edit the `(proposal #<id>)` tag, since the evening deploy routine uses it to link the
Changelog entry back to the proposal that produced it.

<!-- No items yet. -->
Build: Safe Photo's to send (proposal #8): Add a way to select photos on a vehicle's page and export them as a PDF or a zip of JPEGs, each one carrying its stage, part, date, and comment thread, so staff can hand a client a clean set of photos without screenshotting or forwarding raw uploads. This is a download only, not an email sender — staff attach the file wherever they already message clients. When exporting, staff can choose whether to include the damage-mark pins burned onto the photo or just the plain image with its text comments.
