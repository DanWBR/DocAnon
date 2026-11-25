---
layout: default
title: O que é detectado
nav_order: 5
---

# O que é detectado
{: .no_toc }

1. TOC
{:toc}

O DocAnon combina vários detectores. Cada um tem um ponto forte, e o resultado final passa por uma etapa que resolve sobreposições e por outra que espalha cada valor encontrado pelo resto do documento.

## Identificadores com dígito verificador

Quando um identificador tem dígito verificador, o DocAnon confere o dígito antes de marcar o valor. Se o formato estiver certo e o dígito não conferir, o valor ainda é marcado, porque um CPF com erro de digitação continua sendo pessoal, mas com confiança menor.

| Tipo | Formato | Validação |
|---|---|---|
| CPF | `000.000.000-00` ou 11 dígitos | dois dígitos verificadores |
| CNPJ | `00.000.000/0000-00` ou alfanumérico, como `12.ABC.345/01DE-35` | dígitos verificadores da Receita Federal |
| Cartão | 13 a 19 dígitos | algoritmo de Luhn e prefixo de bandeira |
| PIS/PASEP/NIT | `000.00000.00-0` ou 11 dígitos com contexto | dígito verificador |
| Título de eleitor | 12 dígitos com contexto | dígitos verificadores e código da UF |
| CNH | 11 dígitos com contexto | dígitos verificadores |
| Processo judicial | `0000000-00.0000.0.00.0000` | módulo 97 do CNJ |
| IBAN | código de país e até 34 caracteres | módulo 97 |

Os números sem formatação só são marcados como CPF quando o dígito confere. Se não conferir, o DocAnon só os marca quando a palavra "CPF" aparece logo antes.

## Formatos com contexto

Alguns dados têm formatos que se confundem com números comuns. Nesses casos, o DocAnon só marca o valor quando uma palavra de contexto aparece nos 40 caracteres anteriores, e a palavra precisa começar uma palavra do texto, para que "ag" não seja encontrado dentro de "tag".

| Tipo | Palavras de contexto |
|---|---|
| RG | RG, registro geral, identidade |
| Conta bancária | conta corrente, conta poupança, agência, c/c, banco, account number |
| Data de nascimento | nascido em, data de nascimento, DOB, date of birth |
| Passaporte | passaporte, passport |
| Placa antiga (`ABC-1234`) | placa, veículo |
| Chave PIX aleatória | pix, chave |

E-mail, telefone (Brasil e EUA), CEP formatado, placa Mercosul, IP, SSN, OAB e CRM são reconhecidos pelo formato.

## Nomes de pessoas, empresas e lugares

**Modelo de NER:** o DocAnon traz um modelo multilíngue de reconhecimento de entidades (`Davlan/bert-base-multilingual-cased-ner-hrl`) quantizado para rodar na CPU. Ele lê o texto em janelas de 512 tokens e devolve pessoas, organizações e lugares em português e em inglês. Antes de passar o texto ao modelo, o DocAnon converte palavras em caixa alta para iniciais maiúsculas, porque o modelo erra muito com nomes como "RENATO T." em carimbos.

**Prenomes comuns:** como o modelo precisa de contexto de frase, ele perde nomes soltos em células de tabela. Por isso o DocAnon também procura prenomes comuns brasileiros e ingleses, junto com os sobrenomes ou as iniciais que vêm logo depois. Se o prenome vier depois de "São", "Santo" ou "Santa", ele é tratado como lugar.

**Confiança:** se o modelo tiver confiança menor que 55%, o nome é descartado. Entre 55% e 70%, ele aparece desmarcado na revisão.

**Termos técnicos:** o modelo costuma confundir termos de engenharia com pessoas e empresas, como Peng-Robinson, Colebrook, API, ASME, DWSIM e PSV. O DocAnon tem uma lista desses termos e os retira dos resultados do modelo e do LLM. Siglas curtas em caixa alta, com até cinco letras, aparecem desmarcadas.

## Perfil da empresa

Os termos do [perfil](perfis.md) são procurados sem diferença entre maiúsculas e minúsculas e sem diferença de acentos. Se o perfil tiver "Petroquímica Horizonte", o DocAnon também encontra "PETROQUIMICA HORIZONTE". As exceções do perfil nunca são marcadas, qualquer que seja o detector.

## Tags de equipamentos e instrumentos

O DocAnon reconhece tags de equipamento, como `P-101A`, `E-201` e `TK-3001`, e tags ISA de instrumento, como `FIC-2301` e `PSV-2400101`. Por padrão, as tags ficam como estão, porque a IA precisa delas para entender o processo, e uma tag isolada raramente identifica a empresa.

O código de área ou de unidade é diferente, porque ele aponta para uma planta específica. Quando a tag começa com um código, como em `2100-P-101` ou `U-2100`, o DocAnon troca só o código e mantém o resto: `[UNIDADE_1]-P-101`. Os padrões que contam como código de unidade podem ser ajustados no perfil.

Normas e siglas parecidas com tags, como `API-650`, `NR-13` e `ISO-9001`, são ignoradas.

## Quantidades

Os números com unidade são detectados e classificados para a escala. A página [Escala dos números](numeros.md) explica as regras.

## Sugestões do LLM local

Se o Ollama estiver ligado, o modelo local lê o texto em blocos e sugere entidades que os outros detectores perderam, como siglas de órgãos internos, nomes de projetos e referências indiretas ("a planta de Paulínia"). As sugestões entram desmarcadas na revisão. Veja [LLM local](llm-local.md).

## Prioridade entre detectores

Quando dois detectores marcam trechos que se sobrepõem, o DocAnon fica com um só, nesta ordem:

1. marcação manual;
2. identificador com dígito verificador conferido;
3. termo do perfil, valor já anonimizado na sessão e propagação;
4. regras de formato e de contexto;
5. tags;
6. quantidades;
7. NER e prenomes;
8. sugestões do LLM.

Dentro do mesmo nível, o trecho mais longo vence, e depois o de maior confiança.

## Propagação

Depois de resolver as sobreposições, o DocAnon procura no documento inteiro cada valor marcado para receber um marcador. Assim, se o NER reconheceu "Ana Beatriz Rocha" numa frase, as outras ocorrências do nome também são trocadas, mesmo onde o modelo não as reconheceu. Se uma ocorrência estiver coberta por um trecho desmarcado, como uma sigla com baixa confiança, a propagação substitui esse trecho.

Um prenome sozinho só é propagado para ocorrências que começam com maiúscula e que não vêm depois de "São", "Santo" ou "Santa".

## Valores já anonimizados na sessão

Cada documento novo de uma sessão também é comparado com tudo o que a sessão já anonimizou. Se o nome de uma pessoa foi encontrado no relatório, ele também é trocado no desenho do mesmo estudo, mesmo que o OCR do desenho tenha perdido o contexto. Os marcadores continuam os mesmos entre os documentos.

## Ligação entre variantes

O DocAnon tenta reconhecer que valores diferentes são a mesma entidade:

- **Identificadores:** `529.982.247-25` e `52998224725` recebem o mesmo marcador.
- **Pessoas:** "João Carlos da Silva", "João Silva" e "Sr. Silva" recebem o mesmo marcador quando só uma pessoa da sessão combina. Se houver duas pessoas com o mesmo sobrenome, cada variante ambígua recebe um marcador próprio.
- **Empresas:** "Petroquímica Exemplo S.A." e "PETROQUIMICA EXEMPLO" recebem o mesmo marcador, porque os sufixos societários são ignorados.

Quando a reidentificação troca o marcador de volta, ela usa a forma mais completa do nome.
