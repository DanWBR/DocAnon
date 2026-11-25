# Componentes de terceiros

O DocAnon é distribuído junto com os componentes abaixo. Cada um continua sob a sua própria licença.

| Componente | Uso no DocAnon | Licença |
|---|---|---|
| `Davlan/bert-base-multilingual-cased-ner-hrl` (Hugging Face), quantizado em int8 | reconhecimento de nomes de pessoas, organizações e lugares | Academic Free License 3.0 |
| Tesseract OCR 5 e Leptonica (pacote NuGet `Tesseract`) | OCR | Apache 2.0 (Tesseract), BSD-2-Clause (Leptonica) |
| `tessdata_best` (`por.traineddata`, `eng.traineddata`) | modelos de OCR em português e inglês | Apache 2.0 |
| PDFium (via `PDFtoImage` e `bblanchon.PDFium`) | renderização de páginas PDF | BSD-3-Clause / Apache 2.0 |
| PdfPig | leitura da camada de texto de PDFs | Apache 2.0 |
| DocumentFormat.OpenXml | leitura de DOCX, XLSX e PPTX | MIT |
| netDxf | leitura de DXF | MIT |
| SkiaSharp, Svg.Skia | desenho das imagens tarjadas e leitura de SVG | MIT |
| ONNX Runtime, Microsoft.ML.Tokenizers | inferência do modelo de NER | MIT |
| Avalonia, AvaloniaEdit | interface gráfica | MIT |
| CommunityToolkit.Mvvm | interface gráfica | MIT |
| System.CommandLine | linha de comando | MIT |
| .NET runtime | execução | MIT |

O modelo de NER foi treinado por David Adelani e colaboradores sobre o `bert-base-multilingual-cased` do Google. O texto da Academic Free License 3.0 está em https://opensource.org/license/afl-3-0-php.
