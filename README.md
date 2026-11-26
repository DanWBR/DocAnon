# DocAnon

[![Windows installer](https://github.com/DanWBR/DocAnon/actions/workflows/windows-installer.yml/badge.svg)](https://github.com/DanWBR/DocAnon/actions/workflows/windows-installer.yml)

DocAnon anonimiza documentos no seu computador antes que você os envie a um modelo de IA hospedado em outro país (ChatGPT, Claude, Gemini e outros). Ele encontra dados pessoais e segredos industriais e troca cada um por um marcador estável (`[PESSOA_1]`, `[EMPRESA_2]`, `[UNIDADE_1]`). Os números de processo e de negócio são multiplicados por um fator secreto, e o resultado sai como texto e imagens tarjadas. Quando a resposta da IA volta, o DocAnon restaura os nomes reais e desfaz a escala dos números.

Tudo roda localmente. O único acesso de rede possível é ao Ollama neste computador (127.0.0.1), e só quando você o liga nas configurações.

A documentação completa está publicada em [danwbr.github.io/DocAnon](https://danwbr.github.io/DocAnon/), e o texto fonte fica na pasta [`docs/`](docs/index.md): [primeiros passos](docs/primeiros-passos.md), [guia da interface](docs/interface.md), [o que é detectado](docs/deteccao.md), [escala dos números](docs/numeros.md), [desenhos e imagens](docs/imagens.md), [perfis de empresa](docs/perfis.md), [sessões e cofre](docs/sessoes.md), [LLM local](docs/llm-local.md), [formatos](docs/formatos.md), [linha de comando](docs/linha-de-comando.md) e [segurança e limitações](docs/seguranca.md).

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

## Modelos para o Ollama

O DocAnon desliga o modo de raciocínio ("thinking") dos modelos, porque nas tarefas de extração ele deixa a resposta várias vezes mais lenta e pode consumir o espaço da resposta. A comparação abaixo foi feita numa RTX 5070 Ti de 16 GB, com quatro testes: um relatório técnico real de 6 páginas, um relatório fictício com 12 itens sensíveis conhecidos, um desenho fictício com 20 textos conhecidos e o mesmo desenho numa tela 2,5 vezes maior, que imita uma folha A1, onde o texto fica minúsculo depois que o modelo reduz a imagem.

| Modelo | Tamanho | Entidades, relatório real | Entidades, gabarito | Visão, desenho | Visão, folha A1 |
|---|---|---|---|---|---|
| `gemma4:latest` (7,5B) | 6,6 GB | 14 em 17 s | 9/12 em 7 s | 19/20 em 1 s | 9/20 em 1 s |
| `gemma4:12b-it-qat` | 7,2 GB | 13 em 30 s | 10/12 em 8 s | 20/20 em 3 s | 1/20 em 196 s |
| `qwen3.5:9b` | 6,6 GB | 14 em 25 s | 8/12 em 9 s | 19/20 em 5 s | 13/20 em 5 s |
| `qwen3-vl:8b` | 6,2 GB | 0 em 194 s | 9/12 em 88 s | 19/20 em 23 s | 0 a 13/20 em 47 a 117 s |
| `glm-ocr` | 2,7 GB | não se aplica | não se aplica | 7/20 em 4 s (só o carimbo) | 7/20 em 2 s (só o carimbo) |
| `qwen3.6:35b` | 23 GB | 8 em 47 s | não testado | não testado | não testado |

Recomendação:

- **Texto:** `gemma4:latest`, que foi o mais rápido e achou tantas entidades quanto os outros. O `gemma4:12b-it-qat` é uma alternativa equivalente, um pouco mais lenta.
- **Visão:** `qwen3.5:9b`. Foi o único que continuou lendo bem quando o texto ficou pequeno, como numa folha A1 inteira, e no tamanho normal leu tanto quanto os outros.
- O `qwen3-vl:8b` é uma variante que sempre raciocina antes de responder e não aceita desligar isso, por isso fica lento e às vezes não responde.
- O `glm-ocr` é feito para páginas de documento e leu só o carimbo do desenho, embora sem erros.
- O `qwen3.6:35b` não cabe numa GPU de 16 GB e fica lento.

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

## Instalação

Baixe `DocAnon-<versão>-win-x64-setup.exe` da página de [releases](https://github.com/DanWBR/DocAnon/releases) e rode. O instalador já leva o runtime do .NET, o modelo de NER e os dados do Tesseract, então o computador de destino não precisa de mais nada nem de acesso à internet. Por padrão, ele instala para todos os usuários em `Arquivos de Programas`. Se você escolher instalar só para você, ele não pede direitos de administrador. Uma opção do instalador coloca a linha de comando (`docanon`) no PATH.

A desinstalação não apaga os cofres de sessão nem os perfis de empresa em `%LOCALAPPDATA%\DocAnon`. Apague essa pasta se quiser removê-los.

Instalação silenciosa, só para o usuário atual:

```powershell
.\DocAnon-0.1.0-win-x64-setup.exe /VERYSILENT /CURRENTUSER /TASKS="clipath"
```

## Compilação

Requisitos: .NET 10 SDK e Python 3 com os pacotes `onnx` 1.21.0 e `onnxruntime` 1.24.4. O Python só é usado para preparar o modelo de NER, e as versões são fixas porque outras versões gravam um modelo quantizado diferente.

1. Baixe os modelos e prepare o modelo quantizado:

   ```powershell
   pip install onnx==1.21.0 onnxruntime==1.24.4
   .\tools\fetch-models.ps1
   ```

   O script baixa o modelo `Davlan/bert-base-multilingual-cased-ner-hrl` do Hugging Face para `models/ner/source` (709 MB), quantiza o modelo em int8 para `models/ner/model.onnx` e baixa `por.traineddata` e `eng.traineddata` do `tessdata_best` para `models/tessdata`. Os arquivos estão fixados numa revisão de cada repositório e são conferidos pelo SHA-256, por isso todas as compilações levam os mesmos modelos. Se `models/ner/model.onnx` já existir, o script não baixa o modelo de novo. Depois da quantização, você pode apagar `models/ner/source`.

2. Compile e teste:

   ```powershell
   dotnet build DocAnon.slnx
   dotnet test DocAnon.slnx
   ```

3. Gere o instalador (precisa do Inno Setup 6):

   ```powershell
   .\packaging\windows\build-installer.ps1
   ```

   O script lê a versão de `Directory.Build.props`, a menos que você passe `-Version`. Se faltar algum modelo, ele roda `tools\fetch-models.ps1` antes. Depois, ele publica o app em `build\win-x64` e a linha de comando em `build\win-x64\cli`, confere se os modelos e as bibliotecas nativas estão no pacote e grava o instalador em `dist\`. Com `-SkipInstaller`, ele para depois da publicação.

### Integração contínua

O workflow `.github/workflows/windows-installer.yml` roda no GitHub Actions a cada push para `master`, a cada pull request e quando você o dispara à mão na aba Actions. Ele baixa os modelos (que ficam em cache entre as execuções), roda os testes e gera o instalador, que fica disponível como artefato da execução com a versão `<versão>-ci.<número da execução>`.

Quando você envia uma tag `v<versão>`, o workflow também cria uma release no GitHub com o instalador anexado:

```powershell
git tag v0.1.2
git push origin v0.1.2
```

O modelo de NER e o tessdata são copiados para `models\` ao lado do executável. A linha de comando instalada em `cli\` usa a mesma pasta `models\` do app.

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
