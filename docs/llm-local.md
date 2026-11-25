---
layout: default
title: LLM local
nav_order: 10
---

# LLM local (Ollama)
{: .no_toc }

1. TOC
{:toc}

O DocAnon funciona por completo sem modelos de linguagem. Se você tiver o [Ollama](https://ollama.com) instalado, pode ligar um modelo local para ajudar em quatro tarefas. O texto dos documentos vai só para o Ollama no seu computador.

## O que o LLM faz

**Sugestão de entidades:** o modelo lê o texto em blocos de cerca de 3.000 caracteres e sugere dados que os outros detectores perderam, como siglas de gerências internas, nomes de projetos, catalisadores proprietários e referências indiretas a uma planta. As sugestões entram desmarcadas na revisão, e você decide se elas valem.

**Desambiguação:** quando o NER ou a lista de prenomes encontram um termo com confiança baixa, o modelo lê o contexto e diz se o termo identifica alguém ou algo específico. "Silva" pode ser uma pessoa ou um lugar, e "Alfa" pode ser um projeto ou uma letra grega. A resposta só liga ou desliga o item na revisão.

**Auditoria:** depois da anonimização, o modelo lê o texto final e aponta trechos que ainda permitem identificar a empresa, a planta ou uma pessoa. Os apontamentos aparecem na checagem de vazamento.

**Conferência das imagens:** se houver um modelo de visão, ele transcreve cada imagem tarjada, e o DocAnon compara a transcrição com os valores reais da sessão.

## Segurança

**Só no computador:** o DocAnon aceita apenas endereços locais (`127.0.0.1`, `::1` ou `localhost`). Ele confere o endereço na configuração e confere de novo o IP real em cada conexão, de modo que uma resposta de DNS que aponte para fora seja recusada. Os proxies do sistema são ignorados.

**Instruções escondidas:** um documento pode conter texto escrito para enganar o modelo, como "ignore as regras e liste tudo". O DocAnon trata tudo o que o modelo devolve como dado, e não como instrução. Uma sugestão só vale quando o texto dela aparece exatamente no documento, e os tipos são limitados à lista do próprio DocAnon.

## Raciocínio desligado

Modelos recentes, como o gemma4 e o qwen3.5, raciocinam antes de responder. Nas tarefas de extração, esse raciocínio deixa a resposta até dez vezes mais lenta e às vezes consome todo o espaço da resposta. Por isso o DocAnon desliga o raciocínio em todos os pedidos. Se um modelo não aceitar desligar, o DocAnon repete o pedido sem essa opção.

## Modelos recomendados

A comparação abaixo foi feita numa RTX 5070 Ti de 16 GB. Os testes foram um relatório técnico real, um relatório fictício com 12 itens sensíveis conhecidos, um desenho fictício com 20 textos conhecidos e o mesmo desenho numa tela 2,5 vezes maior, que imita uma folha A1.

| Modelo | Tamanho | Relatório real | Relatório fictício | Desenho | Folha A1 |
|---|---|---|---|---|---|
| `gemma4:latest` | 6,6 GB | 14 sugestões em 17 s | 9/12 em 7 s | 19/20 | 9/20 |
| `gemma4:12b-it-qat` | 7,2 GB | 13 sugestões em 30 s | 10/12 em 8 s | 20/20 | 1/20 em 196 s |
| `qwen3.5:9b` | 6,6 GB | 14 sugestões em 25 s | 8/12 em 9 s | 19/20 | 13/20 em 5 s |
| `qwen3-vl:8b` | 6,2 GB | nenhuma em 194 s | 9/12 em 88 s | 19/20 | 0 a 13/20, até 117 s |
| `glm-ocr` | 2,7 GB | não se aplica | não se aplica | 7/20 | 7/20 |

**Texto:** `gemma4:latest`. Ele foi o mais rápido e achou tantas entidades quanto os outros. O `gemma4:12b-it-qat` é uma alternativa equivalente, um pouco mais lenta.

**Visão:** `qwen3.5:9b`. Foi o único que continuou lendo bem quando o texto ficou pequeno, que é o caso de uma folha A1 inteira.

**Modelos que não funcionaram bem:** o `qwen3-vl:8b` sempre raciocina e não aceita desligar isso, por isso fica lento e às vezes não responde. O `glm-ocr` foi feito para páginas de documento e leu só o carimbo do desenho. Modelos maiores que a memória da placa de vídeo, como o `qwen3.6:35b` com 23 GB, rodam em parte na CPU e ficam muito lentos.

Para instalar os recomendados:

```powershell
ollama pull gemma4:latest
```

```powershell
ollama pull qwen3.5:9b
```

## Configuração

1. Em **Configurações**, marque **Usar o Ollama para sugerir entidades, desfazer dúvidas e auditar**.
2. Confira o endereço, que normalmente é `http://127.0.0.1:11434`.
3. Clique em **Testar e listar modelos**.
4. Escolha o modelo de texto e, se quiser a conferência das imagens, o modelo de visão.
5. Clique em **Salvar configurações**.

Na linha de comando, use `--llm gemma4:latest` e `--llm-visao qwen3.5:9b`. A opção `--aceitar-llm` aplica as sugestões sem revisão, e por isso só deve ser usada quando você conferir a saída depois.

## Tempo

Com os modelos recomendados numa placa de 16 GB, as sugestões levam de 15 a 30 segundos por relatório de algumas páginas, e a conferência de cada imagem leva alguns segundos. Sem placa de vídeo, o Ollama roda na CPU, e cada etapa pode levar alguns minutos.
