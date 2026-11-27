# Notices and attribution

The code of dm-companion-live is under the MIT licence (see `LICENSE`). The SRD monster data in
`src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json` is not code and is licensed as follows.

## System Reference Document 5.2.1

### English

This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

### Español

Esta obra incluye material procedente del documento de referencia del sistema 5.2.1 ("SRD 5.2.1") de Wizards of the Coast LLC, disponible en https://www.dndbeyond.com/srd. La licencia sobre el SRD 5.2.1 se concede de acuerdo con la licencia internacional de atribución/reconocimiento 4.0 de Creative Commons, disponible en https://creativecommons.org/licenses/by/4.0/legalcode.

dm-companion-live is not affiliated with, endorsed, sponsored or approved by Wizards of the Coast LLC.

## Sources and modifications

- **Spanish text and all numbers:** compendio-srd (https://github.com/Ismaelbase/compendio-srd, MIT tooling), commit `2d61b5dcabe91ab69d5c4a5a41822eb83c2038cc`, folder `datos/`, as normalised by dm-companion (`src/DmCompanion.Core/Glossary/Packs/criaturas.json` at commit `b95f1e65795d752d8ab167c60ff55751b38ec75b`).
- **English text:** TidyTuesday 2025-05-27 monsters dataset (https://github.com/rfordatascience/tidytuesday/tree/main/data/2025/2025-05-27), commit `97a250500c85463e590b23f9e195c58232a04584`, extracted from the SRD 5.2.1 PDF.
- **Modifications:** both sources are merged by English name (330 creatures, all present in both), converted to plain text (formatting and wrapped lines removed, hyphenated line breaks joined), split into language-neutral sections, and stored as JSON. AC, HP, hit dice, initiative and CR come from the Spanish source.
- Regenerate with `tools/DmCompanionLive.SrdImport` (see `README.md`).

The compendio-srd tooling is distributed under the MIT licence:

    MIT License

    Copyright (c) 2026 Ismael Sevilla

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
