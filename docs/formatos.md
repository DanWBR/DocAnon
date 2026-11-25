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
| PDF | `.pdf` | camada de texto ou OCR | páginas de desenho, páginas escaneadas e imagens grandes | não |
| Word | `.docx`, `.docm`, `.dotx` | sim | imagens embutidas | não |
| Excel | `.xlsx`, `.xlsm` | sim | não | não |
| PowerPoint | `.pptx`, `.pptm` | sim | imagens dos slides | não |
| Imagens | `.png`, `.jpg`, `.jpeg`, `.tif`, `.tiff`, `.bmp`, `.gif`, `.webp` | OCR | sim | não |
| Texto | `.txt`, `.md`, `.markdown`, `.log`, `.json`, `.xml` | sim | não | não |
| CSV | `.csv` | tabela | não | não |
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

Quando você clica em **Salvar pacote**, o DocAnon grava na pasta escolhida:

| Arquivo | Conteúdo |
|---|---|
| `<nome>.anon.md` | o texto anonimizado |
| `<nome>.anon.p01.png`, `p02`... | uma imagem tarjada por página ou imagem |
| `<nome>.anon.imagens.pdf` | as mesmas imagens num PDF sem camada de texto |
| `<nome>.anon.vsdx` | só para Visio, a cópia anonimizada |
