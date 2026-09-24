# Template: person

Path: `/osoby/<firstname_surname>`. Lowercase ASCII, an underscore instead of the space
(`jan_kowalski`). No extension.

Content: plain ASCII text. The first line is the person's full name, capitalised as a name is. Then
one sentence with exactly one markdown link to the city whose trade this person runs.

```
Jan Kowalski
Odpowiada za handel w miescie [Komarowo](/miasta/komarowo).
```

## Rules

- One person per file and one city per person.
- The full name is required. The diary may give the surname in one entry and the first name in a
  later one about the same city and the same matter; join them into one person.
- The author of the diary can be one of these people too, when the diary says so.
- The link target must be an existing city file, written as an absolute path.
- No Polish letters anywhere in the file.
