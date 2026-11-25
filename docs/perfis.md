---
layout: default
title: Perfis de empresa
nav_order: 8
---

# Perfis de empresa
{: .no_toc }

1. TOC
{:toc}

O perfil de empresa é um glossário dos nomes sigilosos de um cliente. Ele vale para todas as sessões, e por isso você o monta uma vez e o reaproveita em todos os documentos daquele cliente. Como a própria lista é sigilosa, o perfil é salvo cifrado, com a mesma proteção dos cofres de sessão.

## Termos

Cada termo tem um texto, um tipo e, se houver, variantes. Os tipos mais usados são Empresa, Cliente, Fornecedor, Planta, Unidade, Local, Projeto, Contrato, Produto e Catalisador.

| Termo | Tipo | Variantes |
|---|---|---|
| Petroquímica Horizonte | Empresa | PQH; Horizonte Petroquímica |
| Refinaria Atlântica | Cliente | REFAT |
| Pólo Itaguaçu | Planta | Polo Itaguaçu |
| Projeto Boreal | Projeto | |
| KT-955 Ultra | Catalisador | KT-955 |

Os termos e as variantes são encontrados sem diferença entre maiúsculas e minúsculas e sem diferença de acentos, e sempre recebem um marcador. Quando dois termos se sobrepõem, o mais longo vence, de modo que "Refinaria Atlântica Sul" não é cortado em "Refinaria Atlântica".

Um termo também pode ser adicionado durante a revisão: selecione o trecho no texto, clique com o botão direito e escolha **Adicionar ao perfil da empresa como**.

## Padrões

Alguns dados seguem um formato, e não uma lista fixa, como números de contrato ou o código de área dentro dos números de documento. Para esses casos, o perfil aceita padrões: expressões regulares com um tipo. No aplicativo, escreva um padrão por linha no formato `TIPO: expressão`.

| Padrão | O que encontra |
|---|---|
| `CONTRATO: \b\d{4}\.\d{7}\.\d{2}\.\d\b` | números de contrato como `1234.0012345.21.7` |
| `UNIDADE: (?<=\b[A-Z]{1,3}-\d{4}[.-]\d{2}-)\d{4}(?=-\d{3}-)` | o código de área de `DE-0001.02-2400-200-GNE-001`, que é `2400` |
| `UNIDADE: \bSF-\d{3}\b` | códigos de sistema como `SF-105` |

Tudo o que a expressão encontra recebe um marcador do tipo indicado. Por isso, quando só uma parte do texto é sigilosa, use `(?<=...)` e `(?=...)` para exigir o contexto sem incluí-lo no trecho, como no exemplo do código de área. Se uma expressão estiver errada, o DocAnon a ignora e continua com as outras.

## Exceções

As exceções são termos que nunca devem ser anonimizados, qualquer que seja o detector. Elas servem para os falsos positivos que aparecem sempre nos documentos do cliente, como o nome de um componente químico, de uma norma interna ou de um programa. Escreva uma exceção por linha, ou use **Nunca anonimizar este termo** na revisão.

## Tags

**Padrões de prefixo de unidade:** por padrão, o DocAnon trata como código de unidade qualquer prefixo numérico antes de uma tag, como o `2100` de `2100-P-101`. Se o cliente usar outro padrão, escreva expressões regulares, uma por linha, que descrevam os prefixos que contam como unidade. Quando a lista estiver preenchida, só os prefixos que combinarem com ela serão anonimizados.

**Anonimizar todas as tags:** se o cliente considerar sigilosa a própria numeração dos equipamentos, marque esta opção. Todas as tags passam a receber marcadores, como `[TAG_EQUIP_1]` e `[TAG_INSTR_3]`.

## Logotipos

O DocAnon procura cada logotipo do perfil em todas as páginas exportadas como imagem, em vários tamanhos, e cobre as ocorrências. Para preparar o recorte:

1. Gere a imagem de uma página que tenha o logotipo. Na linha de comando, use:

   ```powershell
   docanon render desenho.pdf --pagina 1 --png pagina1.png
   ```

2. Abra `pagina1.png` num editor de imagens e recorte só o logotipo. Deixe pouca margem branca em volta dele.
3. Em **Configurações**, clique em **Adicionar logotipo (PNG recortado)** e escolha o recorte.
4. Clique em **Salvar perfil**.

O recorte fica guardado dentro do perfil cifrado. A busca tolera mudanças de tamanho, brilho e contraste, mas não tolera rotação. Se o logotipo aparecer girado em alguns desenhos, adicione também um recorte girado.

## Regiões fixas

Uma região fixa é um retângulo em frações da página, de 0 a 1, medido a partir do canto superior esquerdo. Ele é coberto em todas as páginas exportadas como imagem que tenham a orientação indicada. A região é útil para o carimbo de um padrão de folha, quando o OCR não consegue ler parte dele.

| Campo | Exemplo | Significado |
|---|---|---|
| rótulo | carimbo | nome para você reconhecer a região |
| X, Y | 0,82 e 0,80 | canto superior esquerdo, em frações da largura e da altura |
| largura, altura | 0,18 e 0,20 | tamanho, em frações da largura e da altura |
| orientação | paisagem | a região só vale para folhas nessa orientação |

## Imagens pequenas

A opção **Cobrir imagens pequenas nos desenhos** cobre, nas páginas de desenho, todas as imagens embutidas que ocupam menos de 15% da folha. Ela fica ligada por padrão, porque essas imagens costumam ser logotipos, carimbos e assinaturas.

## Cópia de segurança

O perfil gravado pelo aplicativo fica em `%LOCALAPPDATA%\DocAnon\profiles`. Se os cofres usam a proteção do usuário do Windows, esse arquivo só abre com o seu usuário naquele computador, e ele se perde se você trocar de computador ou reinstalar o Windows. Por isso, guarde uma cópia exportada:

1. Em **Configurações**, carregue o perfil e clique em **Exportar perfil...**.
2. Escolha onde gravar a cópia, por exemplo um pendrive, uma pasta de backup ou um cofre de senhas que aceite anexos.
3. Escolha uma senha de pelo menos 8 caracteres e digite-a duas vezes.

A cópia é cifrada com AES-256-GCM e com uma chave derivada da senha, e ela abre em qualquer computador que tenha o DocAnon. Sem a senha, a cópia não abre, por isso guarde a senha num lugar separado do arquivo.

Para usar a cópia, clique em **Importar perfil...**, escolha o arquivo e digite a senha. O DocAnon grava o perfil na pasta de perfis com a proteção configurada no computador. Se já houver um perfil com o mesmo nome, ele é substituído. O mesmo botão também importa perfis em JSON.

Na linha de comando, os comandos equivalentes são:

```powershell
docanon export-profile "Cliente X" --destino E:\backup\cliente-x.docprofile --senha-arquivo <senha>
```

```powershell
docanon import-profile E:\backup\cliente-x.docprofile --senha-arquivo <senha>
```

## Perfil em JSON

A linha de comando aceita um perfil cifrado do aplicativo (`.docprofile`) ou um perfil em JSON. O arquivo JSON fica em texto puro, por isso guarde-o num lugar protegido.

Um perfil em JSON também pode virar um perfil do aplicativo. O comando abaixo grava o perfil cifrado na pasta de perfis e embute os logotipos indicados por caminho, de modo que o arquivo JSON e os recortes deixam de ser necessários. Se já existir um perfil com o mesmo nome, ele é substituído.

```powershell
docanon import-profile cliente-x.json
```

```json
{
  "Name": "Cliente X",
  "Terms": [
    { "Text": "Refinaria Atlântica", "Type": "CLIENTE", "Aliases": ["REFAT"] },
    { "Text": "Projeto Boreal", "Type": "PROJETO" }
  ],
  "Patterns": [
    { "Pattern": "\\b\\d{4}\\.\\d{7}\\.\\d{2}\\.\\d\\b", "Type": "CONTRATO", "Label": "número de contrato" }
  ],
  "Exceptions": [ "metano" ],
  "UnitPrefixPatterns": [ "\\d{4}" ],
  "AnonymizeAllTags": false,
  "CoverSmallImages": true,
  "Logos": [ { "Name": "logo", "Path": "logo.png" } ],
  "FixedRegions": [
    { "Label": "carimbo", "X": 0.82, "Y": 0.80, "Width": 0.18, "Height": 0.20, "Orientation": "Landscape" }
  ]
}
```
