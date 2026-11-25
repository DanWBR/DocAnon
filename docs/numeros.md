---
layout: default
title: Escala dos números
nav_order: 6
---

# Escala dos números
{: .no_toc }

1. TOC
{:toc}

Num documento industrial, os números costumam ser tão sigilosos quanto os nomes. A capacidade de uma unidade, a vazão de uma corrente ou o custo de um equipamento podem revelar a planta e o negócio. Se o DocAnon trocasse cada número por um marcador, a IA não conseguiria fazer contas nem comparar grandezas. Por isso ele multiplica os números por fatores secretos, de modo que as relações entre eles continuem valendo.

## Dois fatores por sessão

Cada sessão sorteia dois fatores independentes, com um gerador criptográfico:

- **fator de extensão** (`k_ext`), para massa, mols, volume, energia, potência e as vazões dessas grandezas;
- **fator monetário** (`k_moeda`), para valores em dinheiro.

O sorteio é log-uniforme entre 0,40 e 2,50, e a faixa de 0,80 a 1,25 é evitada, porque um fator perto de 1 quase não esconde o valor original. O intervalo pode ser mudado em **Configurações**. Os fatores ficam no cofre da sessão, e todos os documentos da sessão usam os mesmos.

## Como cada unidade é tratada

O DocAnon lê a unidade e calcula quantas vezes cada fator aparece nela. O número é multiplicado por `k_ext` elevado ao expoente de extensão e por `k_moeda` elevado ao expoente monetário.

| Exemplo | Grandeza | Fator aplicado | Por quê |
|---|---|---|---|
| `1.200 t/d` | vazão mássica | `k_ext` | massa por tempo |
| `85 MW` | potência | `k_ext` | energia por tempo |
| `4.500 Nm³/h` | vazão volumétrica | `k_ext` | volume por tempo |
| `R$ 45 milhões` | dinheiro | `k_moeda` | valor monetário |
| `US$ 450/t` | preço unitário | `k_moeda / k_ext` | dinheiro dividido por massa |
| `350 kWh/t` | energia específica | nenhum | energia dividida por massa |
| `820 kg/m³` | massa específica | nenhum | massa dividida por volume |
| `350 °C`, `25 bar`, `12 %` | intensivas | nenhum | não dependem do tamanho da planta |
| `500 mm`, `12 m²` | dimensões | nenhum | não crescem em proporção à capacidade |

Como todas as grandezas extensivas são multiplicadas pelo mesmo fator, os balanços de massa e de energia continuam fechando. Se a carga é 1.000 kg/h e o produto e a purga são 850 kg/h e 150 kg/h, os três valores escalados continuam somando certo. As relações entre custo e capacidade também continuam coerentes.

As grandezas intensivas ficam como estão por padrão. Se uma condição de operação for sigilosa, como a temperatura de um reator proprietário, escolha **marcador** na revisão, e o valor vira `[VALOR_1]`.

## Formas de escrever

O DocAnon entende números em português e em inglês, e decide o separador decimal pelo idioma e pelo padrão do documento:

- `1.234,5` e `1,234.5` valem mil duzentos e trinta e quatro e meio;
- `1.234` vale mil duzentos e trinta e quatro num texto em português e um vírgula dois três quatro num texto em inglês;
- faixas como `80-120 t/d` e `80 a 120 t/d` têm os dois números escalados;
- valores monetários como `R$ 12,5 milhões`, `US$ 3,4 MM`, `MMUS$ 12` e `450 USD/t` são reconhecidos com a moeda antes ou depois do número.

Um número só é escalado quando a unidade vem logo depois dele e está na mesma linha. Números sem unidade, como cotas de desenho, ficam como estão.

## Tabelas

Em tabelas, a unidade costuma ficar no cabeçalho, e não em cada célula. O DocAnon lê a unidade do cabeçalho da coluna (`Vazão (kg/h)`, `Custo [MM US$]`), de uma coluna chamada Unidade ou do rótulo da linha (`Vazão molar (kmol/h)`) e escala as células numéricas de acordo. Isso vale para tabelas de documentos Word, de planilhas e da tabela de correntes das simulações do DWSIM.

## Precisão

O valor escalado mantém pelo menos quatro algarismos significativos e o mesmo estilo de separadores do original. Com menos algarismos, o arredondamento quebraria os balanços e perderia informação na volta. Por exemplo, se o fator fosse 0,5, o valor 45 viraria 22,5; com dois algarismos, ele seria escrito como 23 e voltaria como 46.

## Volta dos números

Na reidentificação, o DocAnon procura números com unidade na resposta da IA e divide cada um pelo fator correspondente. Se a IA repetiu um número que estava no documento, o DocAnon devolve o texto original exato, porque a sessão guarda o par entre o valor escalado e o original. Se a IA calculou um número novo, o DocAnon o converte pela unidade e o mostra na lista como "número convertido", para que você o confira.

## Cuidado com valores públicos

Se o documento citar um valor que alguém pode conhecer por outra fonte, como a capacidade nominal anunciada da planta, esse valor permite descobrir o fator e, a partir dele, todos os outros números. Nesses casos, troque o valor por um marcador na revisão.
