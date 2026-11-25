---
layout: default
title: Início
nav_order: 1
description: "DocAnon anonimiza documentos no seu computador antes que eles sejam enviados a modelos de IA na nuvem."
permalink: /
---

# DocAnon
{: .fs-9 }

O DocAnon anonimiza documentos no seu computador antes que você os envie a um modelo de IA hospedado em outro país. Quando a resposta da IA volta, ele restaura os nomes e os números reais.
{: .fs-6 .fw-300 }

[Primeiros passos](primeiros-passos.md){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[Instalação](instalacao.md){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## Para que serve

Quando alguém cola um contrato, um laudo, um relatório técnico ou um fluxograma no ChatGPT, no Claude ou no Gemini, os dados pessoais e os segredos industriais do documento saem do controle da empresa. No caso dos dados pessoais, esse envio ainda é uma transferência internacional que a LGPD restringe. O DocAnon resolve o problema antes do envio, e todo o processamento acontece no seu computador.

O fluxo tem quatro passos:

1. O DocAnon lê o documento e encontra os dados sensíveis, como nomes de pessoas, CPF, CNPJ, nomes de clientes, plantas, unidades, projetos, tags com código de área e logotipos.
2. Você revisa a lista do que será alterado e corrige o que for preciso.
3. O DocAnon troca cada dado por um marcador estável, como `[PESSOA_1]` ou `[EMPRESA_2]`. Ele também multiplica as vazões, as capacidades e os custos por fatores secretos e cobre com tarjas o texto sensível dos desenhos. Depois disso, ele confere se algum valor real ainda aparece no texto ou nas imagens.
4. Você cola o resultado na IA. Quando a resposta voltar, cola a resposta no DocAnon, e ele devolve o texto com os nomes reais e os números na escala original.

## Princípios

**Tudo é local:** o DocAnon lê, detecta, anonimiza e verifica sem acessar a internet. O único acesso de rede possível é ao Ollama no próprio computador (127.0.0.1), e isso só acontece se você ligar o LLM local nas configurações.

**Você revisa antes de enviar:** nenhum detector automático é perfeito. Por isso o DocAnon mostra tudo o que encontrou, deixa você marcar ou desmarcar cada item e só libera a cópia depois que a checagem de vazamento passa ou depois que você confirma que revisou os apontamentos.

**A anonimização é reversível só para você:** o mapa entre marcadores e valores reais fica num cofre cifrado no seu computador. Se a IA ou qualquer outra pessoa receber o texto anonimizado, ela não consegue desfazer a troca.

**Os números continuam fazendo sentido:** como as grandezas extensivas são multiplicadas pelo mesmo fator, os balanços de massa e de energia continuam fechando. Assim, a IA consegue analisar o processo sem conhecer a escala real da planta.

## Para quem

O DocAnon foi pensado para engenheiros, consultores e analistas que querem usar modelos de IA em documentos técnicos e corporativos sem expor dados pessoais nem informações sigilosas de clientes. Ele lê relatórios, planilhas, apresentações, desenhos de engenharia, imagens e simulações do DWSIM.

## Onde continuar

| Página | Conteúdo |
|---|---|
| [Instalação](instalacao.md) | instalador, requisitos e onde ficam os dados |
| [Primeiros passos](primeiros-passos.md) | um exemplo completo, do documento à resposta reidentificada |
| [Guia da interface](interface.md) | cada aba e cada botão do aplicativo |
| [O que é detectado](deteccao.md) | tipos de dados, regras e prioridades |
| [Escala dos números](numeros.md) | como os números são escalados e restaurados |
| [Desenhos e imagens](imagens.md) | OCR, tarjas, logotipos e regiões fixas |
| [Perfis de empresa](perfis.md) | glossário de termos sigilosos, exceções e logotipos |
| [Sessões e cofre](sessoes.md) | marcadores compartilhados e criptografia |
| [LLM local](llm-local.md) | uso opcional do Ollama e modelos recomendados |
| [Formatos suportados](formatos.md) | o que é lido em cada tipo de arquivo |
| [Linha de comando](linha-de-comando.md) | comandos, opções e exemplos |
| [Segurança e limitações](seguranca.md) | o que o DocAnon protege e o que ele não cobre |
