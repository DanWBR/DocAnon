---
layout: default
title: Desenhos e imagens
nav_order: 7
---

# Desenhos e imagens
{: .no_toc }

1. TOC
{:toc}

Um fluxograma exportado só como texto perde o desenho, e a IA deixa de entender o processo. Por isso o DocAnon exporta os desenhos de duas formas: como texto estruturado, com os rótulos e as tags em lista, e como imagem tarjada, onde cada texto sensível fica coberto por uma tarja com o marcador.

## Quais páginas viram imagem

| Origem | Quando vira imagem |
|---|---|
| PDF com texto | quando a página parece um desenho: muitos traços vetoriais e rótulos curtos espalhados |
| PDF sem camada de texto | sempre; se a página tiver muitos traços, ela é tratada como desenho, e se não tiver, como página escaneada |
| Imagem grande dentro de uma página de texto do PDF | quando ocupa mais de 10% da página; o DocAnon recorta a imagem e a trata à parte |
| Imagens dentro de DOCX e PPTX | sempre; imagens com menos de 300 pixels no maior lado começam desmarcadas |
| PNG, JPG, TIFF | sempre |
| DXF e SVG | sempre; o DocAnon redesenha o arquivo com os marcadores no lugar do texto |

Na linha de comando, a opção `--diagramas` força todas as páginas de PDF a serem tratadas como desenho (`todas`) ou nenhuma (`nunca`).

## OCR

Quando o texto não está disponível como texto, o DocAnon usa o Tesseract com os modelos de português e de inglês.

**Três orientações:** desenhos têm texto vertical, como os números de documento ao longo da borda. Como o Tesseract só lê texto horizontal, o DocAnon lê cada desenho três vezes: uma com a imagem em pé e duas com a imagem girada 90° para cada lado. Depois, ele junta os resultados. Quando duas leituras se sobrepõem, a de maior confiança vence.

**Texto esparso:** para desenhos, o DocAnon usa o modo do Tesseract que procura texto espalhado e não tenta montar parágrafos.

**Imagens pequenas:** se o maior lado de uma imagem tiver menos de cerca de 1.700 pixels, o DocAnon a amplia duas ou três vezes antes do OCR, porque texto pequeno é lido com muitos erros.

**Confiança baixa:** se a confiança média do OCR de uma imagem ficar abaixo de 60%, o DocAnon mostra um aviso, e vale a pena conferir essa imagem com mais atenção.

Quando uma página de PDF tem camada de texto, o DocAnon usa esse texto e roda o OCR só para encontrar o que não está nela, como texto dentro de imagens embutidas no desenho.

## Tarjas

Cada palavra da imagem é ligada ao trecho correspondente do texto. Quando um trecho recebe um marcador ou é escalado, todas as palavras ligadas a ele são cobertas:

- **Marcadores:** tarja escura com o marcador em branco, como `[PESSOA_1]`.
- **Números escalados:** tarja amarela clara com o valor escalado em preto.
- **Parte de uma palavra:** quando só um pedaço de uma palavra é sensível, como o código de área dentro de `DE-0001.02-2400-200-GNE-002`, a tarja cobre só esse pedaço.

A imagem exportada é sempre uma imagem nova e não tem camada de texto. Por isso nenhum texto escondido, metadado ou objeto sob a tarja acompanha a imagem.

## Logotipos

Logotipos identificam o cliente tanto quanto o nome, e o DocAnon tem três maneiras de cobri-los.

**Imagens pequenas nos desenhos:** num desenho em PDF, imagens embutidas pequenas costumam ser logotipos, carimbos ou assinaturas escaneadas. O DocAnon cobre todas elas por padrão. A opção **Cobrir imagens pequenas nos desenhos** do perfil desliga esse comportamento.

**Logotipo do perfil:** muitos logotipos são desenhados com traços vetoriais, e não com uma imagem embutida. Para esses casos, recorte o logotipo de uma página e adicione o recorte ao perfil. O DocAnon procura o logotipo em todas as páginas, em vários tamanhos, e cobre cada ocorrência com uma tarja `[LOGO]`. A página [Perfis de empresa](perfis.md) mostra como recortar o logotipo.

**Regiões fixas:** quando todos os desenhos de um cliente seguem o mesmo padrão de folha, o carimbo e o logotipo ficam sempre no mesmo lugar. O perfil pode guardar essas regiões como frações da página, e elas são cobertas em todas as folhas com a mesma orientação.

Na sub-aba **Imagens** da revisão, essas tarjas aparecem em azul, e um clique liga ou desliga cada uma.

## Checagem de vazamento nas imagens

Depois de gerar as imagens, o DocAnon roda o OCR de novo sobre cada imagem tarjada, nas três orientações, e procura os valores reais da sessão e do perfil. Se ainda conseguir ler algum valor, ele cobre o lugar com uma tarja e o marcador correspondente e confere de novo, até duas vezes. Se o valor continuar legível depois disso, ele aparece na lista da checagem, e a cópia fica bloqueada.

Com o LLM local e um modelo de visão, o DocAnon também pede ao modelo que transcreva cada imagem tarjada e compara a transcrição com os valores reais.

## Tarjas manuais

Se você encontrar algo que não deveria sair, como uma assinatura desenhada à mão, arraste sobre a região na sub-aba **Imagens**. A tarja preta vale só para essa página. Para remover, clique com o botão direito sobre ela.

## Limitações

- Um texto que o OCR não consegue ler em nenhuma das três orientações também não é verificado. Isso acontece com texto muito pequeno, com fontes estilizadas ou com texto encostado em linhas da tabela do carimbo. Para esses casos, use as regiões fixas do perfil ou desenhe a tarja à mão.
- O DocAnon não desenha arquivos Visio. Para obter a imagem tarjada, exporte o desenho para PDF ou SVG e abra esse arquivo.
- Num SVG com imagens raster embutidas, o texto dessas imagens não pode ser verificado, e por isso a imagem tarjada do SVG não é exportada.
