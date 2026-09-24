# Template: city

Path: `/miasta/<name>`. The city's name in the nominative, lowercase ASCII (`komarowo`, `skolwin`).
No extension.

Content: one JSON object. Keys are the goods the city needs, values are the quantities as bare
integers, without units.

```json
{"koparka": 3, "cement": 40, "deska": 12}
```

## Rules

- Keys are the good in the singular nominative, lowercase ASCII, one word. The announcements use
  quantities with genitive plurals and units: `3 koparki` becomes `"koparka": 3`, `40 workow cementu`
  becomes `"cement": 40`, `12 desek` becomes `"deska": 12`.
- Only what the city needs belongs here, with the quantity the announcements board states.
  What the city sells is recorded in `/towary`, not here.
- A city that takes part in the trade but has no announced needs still gets a file, with `{}`.
- No units, no strings, no nesting, no Polish letters, no markdown fences around the JSON.
