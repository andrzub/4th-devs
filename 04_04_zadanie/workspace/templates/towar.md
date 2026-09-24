# Template: good

Path: `/towary/<name>`. The good in the singular nominative, lowercase ASCII (`koparka`, not
`koparki`; `lopata`, not `łopaty`). No extension.

Content: plain ASCII text. One line per city that sells this good, each with a markdown link to the
city file.

```
Oferuje: [Komarowo](/miasta/komarowo)
Oferuje: [Skolwin](/miasta/skolwin)
```

## Rules

- A file exists for every good that appears as sold in the transaction ledger. A good that is only
  needed and never sold gets no file.
- Every city that sells the good gets a link, and no other city does.
- The link targets must be existing city files, written as absolute paths.
