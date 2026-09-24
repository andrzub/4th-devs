# Map of content: Natan's trade filesystem

Natan's notes are filed into a virtual filesystem with three top-level directories. The directories
already exist; you only create files inside them. Every file is a note of one of three kinds, each
with its own template in `templates/`.

| Directory | One file per | Template | Source of truth in the notes |
|---|---|---|---|
| `/miasta` | city that takes part in the trade | `templates/miasto.md` | quantities: the announcements board |
| `/osoby` | person responsible for a city's trade | `templates/osoba.md` | names and cities: the conversation diary |
| `/towary` | good that at least one city offers for sale | `templates/towar.md` | sellers: the transaction ledger |

## Filesystem limits (from the API's own manual)

- A name matches `^[a-z0-9_]+$`: lowercase ASCII letters, digits and underscore. No capitals, no
  Polish letters, no spaces, no hyphens, no extension.
- A file name has at most 20 characters.
- Names are unique across the whole filesystem: a good and a city cannot share a name, and no file
  may be named like a directory.
- Content is markdown text, and a markdown link must point to a file that already exists.

## Rules that apply everywhere

- JSON keys and every text you write use plain ASCII: a c e l n o s z instead of ą ć ę ł ń ó ś ź ż.
- Names are in the nominative case, the form a reader would look them up by. The notes inflect them
  (a diary written from one city speaks of others in the genitive or locative); the file name is the
  base form, lowercased.
- Goods are named in the singular (`koparka`, not `koparki`), both as file names in `/towary` and as
  keys in a city's JSON.
- A link to a city is a markdown link to its file with an absolute path: `[Komarowo](/miasta/komarowo)`.
  The label may keep the proper capitalisation; the path is the file name.
- Write every note as if the reader had no other context. A person note names the whole person,
  first name and surname, even when the diary gives the two halves in different entries.

## Order of work

1. Read every note in full before writing anything. The same person or city appears in several
   notes and the pieces complement each other.
2. Cities first (they are the link targets), then people, then goods.
3. When two notes disagree, the source of truth from the table above wins. Quantities are copied from
   the announcements board exactly as written there, as bare numbers without units.
4. Before finishing, list each directory and compare it with the notes: every city has exactly one
   person, and every good that appears as sold in the ledger has a file linking to every city that
   sells it.
