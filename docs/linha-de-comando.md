---
layout: default
title: Linha de comando
nav_order: 12
---

# Linha de comando
{: .no_toc }

1. TOC
{:toc}

A linha de comando `docanon` faz o mesmo trabalho do aplicativo, mas não tem a etapa de revisão. Ela serve para processar muitos arquivos de uma vez, para scripts e para conferir o resultado da detecção. Como não há revisão, confira sempre a saída antes de enviar o texto a uma IA.

Se você instalou o DocAnon com a opção de PATH, chame `docanon` de qualquer terminal. Se não, chame o executável `docanon.exe` que fica na pasta `cli` da instalação.

## Comandos

| Comando | O que faz |
|---|---|
| `extract` | mostra o texto extraído, sem detectar nada |
| `detect` | lista os dados sensíveis encontrados, sem alterar nada |
| `anonymize` | anonimiza os documentos e grava o texto, as imagens tarjadas e a sessão cifrada |
| `reidentify` | restaura os valores reais na resposta da IA |
| `sessions` | lista as sessões salvas |
| `profiles` | lista os perfis de empresa salvos |
| `import-profile` | grava um perfil em JSON ou uma cópia exportada como perfil cifrado, que o aplicativo passa a listar |
| `export-profile` | grava uma cópia do perfil cifrada com senha, que abre em qualquer computador |
| `inspect` | mostra as entidades e os fatores de uma sessão |
| `render` | desenha uma página de PDF em PNG, por exemplo para recortar o logotipo |

## Opções

| Opção | Comandos | Significado |
|---|---|---|
| `--sessao <rótulo ou arquivo>` | anonymize, reidentify, inspect | sessão a usar; em `anonymize`, ela é criada se não existir |
| `--senha <senha>` | todos que abrem cofres | senha do cofre; sem ela, o cofre usa a proteção do usuário do Windows |
| `--perfil <arquivo>` | detect, anonymize | perfil de empresa, em JSON ou `.docprofile` |
| `--saida <pasta>` | anonymize | pasta dos arquivos gerados; o padrão é `anonimizado` |
| `--sem-imagens` | anonymize | exporta só o texto |
| `--sem-ner` | detect, anonymize | não usa o modelo de reconhecimento de nomes |
| `--diagramas auto\|todas\|nunca` | extract, detect, anonymize | quais páginas de PDF tratar como desenho |
| `--llm <modelo>` | detect, anonymize | liga as sugestões e a auditoria do Ollama local |
| `--llm-visao <modelo>` | anonymize | liga a conferência das imagens por um modelo de visão |
| `--aceitar-llm` | detect, anonymize | aplica as sugestões do LLM sem revisão |
| `--json` | detect | saída em JSON, com cada trecho, tipo, detector e confiança |
| `--entrada <arquivo>` | reidentify | arquivo que a IA produziu (TXT, MD, CSV, JSON, XML, HTML, DOCX, PPTX, XLSX ou PDF); sem ele, a resposta é lida da entrada padrão |
| `--arquivo-saida <arquivo>` | reidentify | grava o arquivo restaurado nesse caminho; sem ele, o texto de um TXT ou MD vai para a saída padrão, e os outros formatos ganham um arquivo `.restaurado` ao lado do original |
| `--raiz <pasta>` | todos que usam cofres | pasta dos cofres, no lugar de `%LOCALAPPDATA%\DocAnon` |
| `--pagina <n>` e `--png <arquivo>` | render | página a desenhar e arquivo de saída |
| `--destino <arquivo>` | export-profile | arquivo da cópia exportada |
| `--senha-arquivo <senha>` | export-profile, import-profile | senha da cópia exportada; também pode vir de `DOCANON_SENHA_PERFIL` |

A senha também pode vir da variável de ambiente `DOCANON_SENHA`, o que evita deixá-la no histórico do terminal.

## Exemplos

Ver o que seria anonimizado num relatório:

```powershell
docanon detect relatorio.pdf --perfil cliente-x.json
```

Anonimizar um relatório e um desenho na mesma sessão:

```powershell
docanon anonymize relatorio.pdf desenho.pdf --sessao estudo-tocha --perfil cliente-x.json --saida .\para-ia
```

Anonimizar com o LLM local, aceitando as sugestões:

```powershell
docanon anonymize relatorio.docx --sessao estudo-tocha --llm gemma4:latest --llm-visao qwen3.5:9b --aceitar-llm
```

Restaurar a resposta da IA:

```powershell
docanon reidentify --sessao estudo-tocha --entrada resposta.md --arquivo-saida resposta-real.md
```

Restaurar um relatório em Word que a IA escreveu, que fica gravado como `relatorio-ia.restaurado.docx`:

```powershell
docanon reidentify --sessao estudo-tocha --entrada relatorio-ia.docx
```

Transformar um perfil em JSON num perfil cifrado do aplicativo:

```powershell
docanon import-profile cliente-x.json
```

Ver o conteúdo de uma sessão:

```powershell
docanon inspect --sessao estudo-tocha
```

## Códigos de saída

| Código | Significado |
|---|---|
| 0 | tudo certo |
| 2 | `anonymize` terminou, mas a checagem de vazamento encontrou valores reais na saída |
| 3 | `reidentify` terminou, mas a resposta tinha marcadores que não existem na sessão |
| 1 | o comando não pôde ser executado, por exemplo por senha errada ou arquivo inexistente; a mensagem de erro diz o motivo |

Em scripts, trate o código 2 como um sinal para revisar a saída antes de enviá-la.
