# DocAnon

DocAnon anonimiza documentos no seu computador antes que você os envie a um modelo de IA hospedado em outro país (ChatGPT, Claude, Gemini e outros). Ele encontra dados pessoais e segredos industriais e troca cada um por um marcador estável (`[PESSOA_1]`, `[EMPRESA_2]`, `[UNIDADE_1]`). Os números de processo e de negócio são multiplicados por um fator secreto, e o resultado sai como texto e imagens tarjadas. Quando a resposta da IA volta, o DocAnon restaura os nomes reais e desfaz a escala dos números.

Tudo roda localmente. O único acesso de rede possível é ao Ollama neste computador (127.0.0.1), e só quando você o liga nas configurações.

> **Importante:** a detecção automática não é perfeita. Revise sempre a lista de trechos e as imagens antes de copiar o resultado. A checagem de vazamento ajuda, mas não substitui a sua revisão.

## Formatos

| Entrada | Como é lido |
|---|---|
| PDF com texto | camada de texto (PdfPig), com OCR das imagens embutidas |
| PDF escaneado ou desenho vetorial sem texto | OCR (Tesseract por+eng), inclusive texto vertical |
| DOCX, XLSX, PPTX | OpenXML, incluindo tabelas, cabeçalhos, notas, comentários e imagens |
| PNG, JPG, TIFF | OCR |
| TXT, MD, CSV | texto |
| DWSIM (.dwxmz, .dwxml) | resumo com equipamentos, conectividade e tabela de correntes |
| DXF, SVG | texto dos objetos, com o desenho redesenhado com os marcadores |
| Visio (.vsdx) | texto das formas e uma cópia anonimizada do .vsdx |

A saída é Markdown. Diagramas, páginas escaneadas e imagens também saem como PNG e como um PDF que contém só imagens, sem camada de texto.

## O que é detectado

- **Identificadores com dígito verificador:** CPF, CNPJ (inclusive o alfanumérico), cartão, PIS, título de eleitor, CNH, processo CNJ e IBAN.
- **Formatos com contexto:** e-mail, telefone, CEP, RG, conta bancária, placa, chave PIX, OAB, CRM, data de nascimento, IP, SSN e passaporte.
- **Nomes de pessoas, empresas e lugares:** um modelo de NER multilíngue local (`Davlan/bert-base-multilingual-cased-ner-hrl`, licença AFL-3.0) e uma lista de prenomes comuns para carimbos e tabelas.
- **Perfil da empresa:** clientes, plantas, projetos, produtos e catalisadores, com variantes e exceções.
- **Tags de equipamentos e instrumentos:** são mantidas por padrão, mas o prefixo de unidade é anonimizado (`2100-P-101` vira `[UNIDADE_1]-P-101`).
- **Logotipos:** por correspondência com a imagem do perfil, imagens pequenas em desenhos (logotipos, carimbos, assinaturas) e regiões fixas da folha.
- **Sugestões do LLM local (opcional):** entram desmarcadas na revisão.

Quando uma entidade é marcada para anonimizar, o DocAnon a procura no documento inteiro e em todos os documentos da mesma sessão.

## Escala dos números

Cada sessão sorteia dois fatores secretos, um para grandezas extensivas (massa, mols, volume, energia, potência e suas vazões) e outro para dinheiro. Cada unidade recebe a combinação certa: `kg/h` é multiplicado pelo fator extensivo, `R$` pelo fator monetário e `R$/t` pela razão entre os dois. Assim, balanços de massa e energia e relações entre custo e capacidade continuam coerentes para a IA. Temperatura, pressão, composição e outras grandezas intensivas ficam como estão, mas você pode trocá-las por um marcador na revisão.

Se o documento citar um valor público conhecido (a capacidade nominal anunciada da planta, por exemplo), alguém pode deduzir o fator a partir dele. Nesse caso, transforme esse valor em marcador.

## Sessões e cofre

Uma sessão corresponde a uma conversa com a IA. Os documentos de uma sessão compartilham marcadores e fatores. O mapa entre marcadores e valores reais fica em `%LOCALAPPDATA%\DocAnon\sessions`, cifrado com AES-256-GCM. A chave vem do usuário do Windows (DPAPI) ou de uma senha (PBKDF2-SHA256, 600 mil iterações). Os perfis de empresa usam o mesmo esquema. O rótulo da sessão fica no nome do arquivo sem cifra, por isso não use nomes sigilosos nele.

## Usando o aplicativo

1. Crie uma sessão e, se tiver, escolha o perfil da empresa.
2. Arraste os documentos ou cole um texto.
3. Na aba **Revisão**, confira a lista. Itens desmarcados não são alterados. Selecione um trecho no texto e use o botão direito para marcar, adicionar ao perfil ou criar uma exceção. Na sub-aba **Imagens**, clique nas tarjas azuis para ligar ou desligar e arraste para desenhar tarjas novas.
4. Clique em **Anonimizar e verificar vazamentos**. A cópia só é liberada depois que a checagem passa ou depois que você confirma que revisou os apontamentos.
5. Cole a resposta da IA na aba **Reidentificação**.

## Linha de comando

```powershell
docanon detect relatorio.pdf --perfil perfil.json
docanon anonymize relatorio.pdf desenho.pdf --sessao cliente-x --perfil perfil.json --saida .\anonimizado
docanon reidentify --sessao cliente-x --entrada resposta.txt --arquivo-saida resposta-real.md
docanon render desenho.pdf --png pagina1.png     # para recortar o logotipo da empresa
```

Sem `--senha`, o cofre usa a proteção do usuário do Windows. A opção `--llm gemma4:latest` liga as sugestões e a auditoria do Ollama local, e `--llm-visao` liga a conferência das imagens por um modelo de visão.

O perfil em JSON para a linha de comando tem este formato (o arquivo fica em texto puro; no aplicativo, o perfil é salvo cifrado):

```json
{
  "Name": "Cliente X",
  "Terms": [ { "Text": "Empresa Exemplo", "Type": "EMPRESA", "Aliases": ["EMPEX"] } ],
  "Exceptions": [ "metano" ],
  "Logos": [ { "Name": "logo", "Path": "logo.png" } ],
  "FixedRegions": [ { "Label": "carimbo", "X": 0.82, "Y": 0.80, "Width": 0.18, "Height": 0.20, "Orientation": "Landscape" } ]
}
```

## Compilação

Requisitos: .NET 10 SDK e Python 3 com `onnxruntime`, este último só para preparar o modelo de NER.

1. Baixe o modelo de NER e o tessdata e prepare o modelo quantizado:

   ```powershell
   python tools/export_ner_model.py --source models/ner/source --out models/ner --model-id Davlan/bert-base-multilingual-cased-ner-hrl --license afl-3.0
   ```

   `models/ner/source` deve conter `model.onnx`, `vocab.txt`, `config.json` e `tokenizer_config.json` do Hugging Face. `models/tessdata` deve conter `por.traineddata` e `eng.traineddata` do `tessdata_best`.

2. Compile e teste:

   ```powershell
   dotnet build DocAnon.slnx
   dotnet test DocAnon.slnx
   ```

3. Publique:

   ```powershell
   dotnet publish src/DocAnon.App -c Release -r win-x64 --self-contained -o publish/app
   dotnet publish src/DocAnon.Cli -c Release -r win-x64 --self-contained -o publish/cli
   ```

O modelo de NER e o tessdata são copiados para `models/` ao lado do executável.

## Estrutura

| Projeto | Conteúdo |
|---|---|
| `DocAnon.Core` | detectores, unidades e escala, pseudonimização, reidentificação, cofre, perfis, checagem de vazamento |
| `DocAnon.Extraction` | leitores de cada formato |
| `DocAnon.Ocr` | Tesseract, renderização de PDF com PDFium e OCR em três orientações |
| `DocAnon.Ner` | inferência ONNX do modelo de NER |
| `DocAnon.Llm` | cliente do Ollama restrito a loopback e as tarefas do LLM local |
| `DocAnon.Redaction` | tarjas nas imagens, busca de logotipos e PDF só de imagens |
| `DocAnon.Services` | o fluxo completo, usado pelo aplicativo e pela linha de comando |
| `DocAnon.App` | aplicativo Avalonia |
| `DocAnon.Cli` | linha de comando |

## Limitações conhecidas

- O OCR pode ler mal textos muito pequenos, sobrepostos a linhas ou com fontes estilizadas. A exportação cobre automaticamente os valores reais que o OCR ainda consegue ler, mas um texto que o OCR não lê em nenhuma passada também não é verificado.
- O DocAnon não desenha arquivos Visio. Para obter a imagem tarjada, exporte o desenho para PDF ou SVG.
- O Tesseract usado é a versão para Windows. Em Linux e macOS, a parte de OCR precisa de outra biblioteca nativa.
- Em SSDs, apagar um cofre sobrescreve o arquivo, mas o disco pode manter cópias antigas em áreas de reserva. Use criptografia de disco inteiro.
