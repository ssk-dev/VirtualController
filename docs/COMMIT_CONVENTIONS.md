# Git Commit Conventions

This document describes the commit conventions for this repository. They were derived
from the existing commit history (`git log`) and should be used consistently for new
commits.

## Format

```
<type>: - <short, concise description (subject)>

- <detail bullet 1>

- <detail bullet 2>

- <detail bullet n>
```

- **Subject line**: `<type>: - <description>`
  - After the type comes a colon, then a space, then a hyphen (`- `), followed by the
    actual description.
  - The description briefly explains **why** the change was needed and/or **what**
    changes as a result (not just "what" was technically changed).
- **Body**: consists of one or more paragraphs, each starting with `- `
  (one bullet point per paragraph, separated by a blank line).
  - Each bullet describes one self-contained aspect of the change
    (e.g. affected file/class + what was changed there + reasoning if relevant).
  - It is common to combine several thematically different changes (e.g. several
    `fix:`/`feature:` blocks) into a single commit when they are part of the same
    changeset - in that case the respective type prefix is repeated within the body
    (see example 3).

## Allowed types

| Type       | Usage                                                                   |
|------------|--------------------------------------------------------------------------|
| `feature:` | New functionality, new UI/styling, new behavior                        |
| `fix:`     | Bug fix / correction of faulty behavior                                 |

> Note: Earlier in the project's history, `feat:` was sometimes used instead of
> `feature:`. For new commits, please use only `feature:` and `fix:` to keep the
> convention consistent.

## Language

- New commit messages are written **in English** (regardless of the language used in
  the conversation with the assistant).
- Some older commits in the history are in German - this is historical and no longer a
  valid guideline.

## Style rules for the description

- Description in the **imperative** or as a concise statement of state, no punctuation
  at the end of the subject.
- Where possible: **name the reason/cause**, not just the symptom or the code change
  (e.g. for a fix: which problem occurred and why).
- Name concrete affected files/classes/keys in the body when it improves traceability
  (e.g. `Theme.xaml`, `Styles.xaml`, resource keys, method names).
- No scope in parentheses (no `feat(ui): ...`), no footers like `BREAKING CHANGE:`.

## Examples from the history

**Example 1 - simple fix:**
```
fix: - add dark-theme styles for ListView, ListViewItem and GridViewColumnHeader in VirtualController.App/Themes/Styles.xaml
```

**Example 2 - fix with reasoning + detail bullet:**
```
fix: - GitHub-Actions-Build (BG1002) schlug fehl, da assets/logo.png trotz Referenzierung durch VirtualController.App.csproj (eingebettete Resource) und MainWindow.xaml (Header-Logo) nicht versioniert war - .gitignore schloss den gesamten assets/-Ordner aus und machte nur fuer VirtualController.ico eine Ausnahme

- .gitignore um passende !assets/logo.png-Ausnahme ergaenzt und die Datei selbst dem Repository hinzugefuegt
```

**Example 3 - multiple changes in one commit (mixed types in the body):**
```
fix: - Hintergrund an mehreren Stellen faelschlicherweise weiss statt dunkel gerendert, da Background=Transparent die Window-Hintergrundfarbe dort nicht zuverlaessig durchscheinen liess

- Neuer benannter Brush AppBackgroundBrush (#010D19) in Theme.xaml, jetzt explizit auf den betroffenen Bereichen gesetzt statt Transparent/kein Background

feature: - Native Windows-Titelleiste durch eigene, dunkle Titelleiste ersetzt (WindowStyle=None + WindowChrome)

- Neue TitleBarButtonStyle/TitleBarCloseButtonStyle in Styles.xaml
```

**Example 4 - current, English example (feature with several detail bullets):**
```
feature: - redesign main window header and virtual controller cards for a larger, more spacious layout

- enlarge main window (900x1440), make it topmost, shrink custom title bar and hide its icon/title text

- add PanelVirtualControllersBorderBrush/BackgroundBrush (and Active variants) gradient brushes in Theme.xaml, applied to the virtual controller sidebar cards

- add HeaderActionButtonHeight constant and shrink TitleBarButtonStyle size in Styles.xaml
```

> Note: Examples 2 and 3 are kept in their original German wording on purpose, as they
> are direct quotes from the actual (older) commit history. New commits must be written
> in English, as described above.

## Checklist before committing

- [ ] Subject starts with `feature:` or `fix:` followed by `- <description>`
- [ ] Description is in English
- [ ] Body contains, if needed, further `- ` bullets with details/reasoning
- [ ] Affected files/resource keys are named where sensible
- [ ] No scope, no `BREAKING CHANGE:` footer
