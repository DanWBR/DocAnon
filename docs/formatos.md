---
layout: default
title: Formatos suportados
nav_order: 11
---

# Formatos suportados
{: .no_toc }

1. TOC
{:toc}

Todo documento vira um texto em Markdown, e todos os detectores trabalham sobre esse texto. Os formatos com parte visual também geram páginas de imagem, que saem tarjadas na exportação.

## Resumo

| Formato | Extensões | Texto | Imagem tarjada | Cópia nativa |
|---|---|---|---|---|
| PDF | `.pdf` | camada de texto ou OCR | páginas de desenho, páginas escaneadas e imagens grandes | `.pdf` com todas as páginas tarjadas |
| Word | `.docx`, `.docm`, `.dotx` | sim | imagens embutidas | mesma extensão |
| Excel | `.xlsx`, `.xlsm` | sim | não | mesma extensão |
| PowerPoint | `.pptx`, `.pptm` | sim | imagens dos slides | mesma extensão |
| Imagens | `.png`, `.jpg`, `.jpeg`, `.tif`, `.tiff`, `.bmp`, `.gif`, `.webp` | OCR | sim | não |
| Texto | `.txt`, `.md`, `.markdown`, `.log`, `.json`, `.xml` | sim | não | mesma extensão, exceto Markdown |
| CSV | `.csv` | tabela | não | `.csv` |
| DWSIM | `.dwxmz`, `.dwxml` | resumo da simulação | não | não |
| DXF | `.dxf` | sim | desenho redesenhado | não |
| SVG | `.svg` | sim | desenho redesenhado | não |
| Visio | `.vsdx`, `.vsdm` | sim | não | `.vsdx` anonimizado |

## PDF

**Páginas de texto:** o DocAnon lê a camada de texto com o PdfPig e monta os parágrafos pela posição das palavras.

**Páginas de desenho:** quando uma página tem muitos traços vetoriais e rótulos curtos espalhados, o DocAnon a trata como desenho. O texto sai como lista de rótulos, e a página sai também como imagem tarjada. O OCR roda sobre a página para encontrar texto dentro de imagens embutidas no desenho.

**Páginas sem camada de texto:** se a página tem menos de 20 letras de texto, ela foi escaneada ou teve o texto convertido em traços, como acontece em muitos desenhos exportados por programas de CAD. O DocAnon renderiza a página e lê tudo por OCR. Se a página tiver muitos traços, o DocAnon a trata como desenho e roda o OCR em três orientações.

**Páginas giradas:** a rotação da página é levada em conta, para que as tarjas caiam no lugar certo.

**Imagens grandes:** numa página de texto, se uma imagem ocupar mais de 10% da folha, o DocAnon a recorta, roda o OCR nela e a exporta como uma imagem tarjada própria.

## Word

O DocAnon lê o corpo, as tabelas, os cabeçalhos, os rodapés, as notas de rodapé e os comentários. Os comentários entram com o nome do autor, para que o autor também seja anonimizado. Revisões excluídas e códigos de campo são ignorados. As imagens embutidas passam por OCR e saem tarjadas. Imagens com menos de 300 pixels no maior lado começam desmarcadas para exportação, porque costumam ser ícones e logotipos.

## Excel

Cada planilha visível vira uma tabela Markdown com os valores calculados. As fórmulas ficam de fora. Datas são escritas no formato ano-mês-dia. Planilhas ocultas não são incluídas, e o DocAnon avisa quando isso acontece. Se uma planilha passar de 5.000 linhas ou de 80 colunas, o DocAnon a corta nesse limite e também avisa.

As unidades nos cabeçalhos das colunas, como `Vazão (kg/h)` ou `Custo (MM US$)`, são usadas na [escala dos números](numeros.md).

## PowerPoint

Cada slide vira uma seção, com as caixas de texto, as tabelas e as notas do apresentador. As imagens dos slides passam por OCR e saem tarjadas.

## CSV

O separador é escolhido entre ponto e vírgula e vírgula, pelo que aparece mais nas primeiras linhas. O arquivo vira uma tabela Markdown.

## DWSIM

O DocAnon lê o XML da simulação diretamente, sem precisar do DWSIM instalado, e escreve um resumo com:

- nome, autor e comentários da simulação;
- lista de equipamentos com as descrições;
- conectividade entre correntes e equipamentos, como `Carga → P-101`;
- tabela de correntes com temperatura (°C), pressão (kPa), vazões mássica (kg/h), molar (kmol/h) e volumétrica (m³/h), massa específica e massa molar, em blocos de oito correntes;
- lista de componentes;
- nomes, tags e descrições que aparecem dentro das operações unitárias, como os trechos de uma rede de tubulações;
- anotações do fluxograma.

Para a IA, a conectividade explícita ajuda mais que uma imagem do fluxograma. A planilha interna e os scripts da simulação não entram no resumo, e o DocAnon avisa quando eles existem.

## DXF

O DocAnon abre o desenho com a biblioteca netDxf, expande os blocos e lê os textos simples, os textos de várias linhas e os atributos de blocos. A imagem tarjada é um redesenho com linhas, polilinhas, arcos, círculos e textos, em que os marcadores entram no lugar do texto original. Hachuras, cotas e imagens externas não entram no redesenho.

## SVG

Os elementos de texto, título e descrição são listados no Markdown. A imagem tarjada é o próprio SVG com os textos trocados pelos marcadores e depois renderizado. Se o SVG tiver imagens raster embutidas, a imagem tarjada não é exportada, porque o texto dessas imagens não pode ser verificado.

## Visio

O DocAnon lê o texto das formas de todas as páginas e dos mestres. Ele não desenha arquivos Visio, por isso não há imagem tarjada. Em compensação, ele grava uma cópia `.vsdx` com os textos trocados pelos marcadores, com os comentários apagados e com autor, empresa e título removidos das propriedades do arquivo. Para obter também a imagem, exporte o desenho para PDF ou SVG.

## Texto colado

O texto colado na aba **Documentos** é tratado como um documento de texto simples.

## Arquivos exportados

Quando você clica em **Salvar pacote**, o DocAnon grava na pasta escolhida os arquivos abaixo. Como esses arquivos também podem ser anexados na IA, o nome de cada um passa pela mesma anonimização do texto, com os marcadores da sessão. Assim, se o perfil tiver padrões para os números de documento, um desenho chamado `DE-0001.02-2400-200-GNE-001.pdf` sai como `DE-[PROJETO_1].02-[UNIDADE_1]-200-GNE-001.anon.md`.

| Arquivo | Conteúdo |
|---|---|
| `<nome>.anon.md` | o texto anonimizado |
| `<nome>.anon.p01.png`, `p02`... | uma imagem tarjada por página ou imagem |
| `<nome>.anon.imagens.pdf` | as mesmas imagens num PDF sem camada de texto, exceto quando o original é um PDF |
| `<nome>.anon.docx`, `.pptx`, `.xlsx`, `.pdf`, `.csv`, `.txt`, `.vsdx`... | a cópia anonimizada no formato do original |

## Cópia no formato original

Quando o formato tem cópia nativa, o pacote leva uma cópia do arquivo no mesmo formato, para que você possa anexá-la na IA como se fosse o original. A cópia recebe as mesmas decisões da revisão. Cada valor que a revisão anonimizou vira o marcador da sessão em qualquer lugar do arquivo, e um número é escalado quando a revisão escalou o mesmo texto, por exemplo "1.200 t/d" ou "1200" numa coluna "Vazão (kg/h)".

**Word, PowerPoint e Excel:** o DocAnon troca o texto dentro de cada parágrafo, célula, cabeçalho, rodapé, nota, comentário, caixa de texto e slide mestre. Se um marcador cair em dois trechos com formatação diferente, o primeiro trecho recebe o marcador e mantém a sua formatação. Cada imagem que foi exportada é trocada pela versão tarjada. Se uma imagem não foi exportada, ela vira um retângulo cinza. Como a revisão não mostra as outras partes do arquivo, a cópia também apaga os autores do arquivo, dos comentários e das revisões, o texto de revisões excluídas, os números guardados nos gráficos e o conteúdo dos arquivos incorporados. No Excel, as planilhas ocultas ficam vazias, e as fórmulas são recalculadas quando o arquivo é aberto.

**PDF:** um PDF não permite trocar o texto no lugar sem refazer as fontes, por isso a cópia tem uma imagem tarjada para cada página do original, na mesma ordem. As páginas de texto são desenhadas e cobertas onde o texto foi trocado, e cada tarja leva o marcador. As imagens recortadas dessas páginas voltam ao lugar já tarjadas. Depois disso, o OCR lê cada página da cópia, e o DocAnon cobre o que ainda for legível, como nas outras imagens. Se a camada de texto de uma página não for legível, o que acontece com fontes sem a tabela ToUnicode, a página passa pelo OCR como uma página escaneada.

**CSV e texto:** a cópia mantém o separador, as aspas, a codificação e as quebras de linha do original. Num CSV, a unidade do cabeçalho vale para os números da coluna, como numa tabela.

A checagem de vazamento também roda sobre a cópia. Os pontos que ela encontrar aparecem na lista da aba **3. Exportação** com a origem "cópia".

## Arquivos que a IA produz

A IA pode devolver um relatório em Word, uma apresentação, uma planilha ou um PDF com os marcadores. Na aba **4. Reidentificação**, o botão **Restaurar arquivos...** aceita arquivos TXT, Markdown, CSV, JSON, XML, HTML, DOCX, PPTX, XLSX e PDF. O DocAnon troca cada marcador pelo valor real, divide os números escalados pelos fatores da sessão e grava o resultado ao lado do arquivo da IA, no mesmo formato e com `.restaurado` antes da extensão. Se o nome do arquivo tiver marcadores, eles também são restaurados.

**Word, PowerPoint e Excel:** o valor real entra no lugar do marcador e mantém a formatação do trecho. No Excel, os números das células também são convertidos de volta quando a coluna tem a unidade no cabeçalho.

**PDF:** o DocAnon cobre cada marcador com um retângulo branco e escreve o valor real por cima, na mesma posição e no mesmo tamanho de letra. Se o valor real for mais longo que o marcador, a letra diminui até 55 % para caber no espaço até a próxima palavra. O texto original continua por baixo do retângulo, por isso, se você copiar o texto do PDF, o marcador vem junto com o valor real.
