---
layout: default
title: Segurança e limitações
nav_order: 13
---

# Segurança e limitações
{: .no_toc }

1. TOC
{:toc}

## O que o DocAnon protege

**Os dados não saem do computador:** a leitura, a detecção, a anonimização e as checagens rodam localmente. Os modelos de reconhecimento de nomes e de OCR vêm no instalador. O único acesso de rede possível é ao Ollama no próprio computador, e o DocAnon confere em cada conexão que o destino é local.

**O mapa fica cifrado:** o vínculo entre marcadores e valores reais fica em cofres AES-256-GCM, protegidos pelo usuário do Windows ou por uma senha. Os perfis de empresa também ficam cifrados.

**As imagens não carregam texto escondido:** toda imagem exportada é desenhada de novo a partir do original tarjado e não tem camada de texto, metadados nem objetos sob as tarjas.

**A cópia depende da checagem:** antes de liberar o texto e as imagens, o DocAnon procura os valores reais da sessão e do perfil no resultado, roda os detectores de dados pessoais de novo e lê as imagens tarjadas por OCR. Se alguma dessas etapas encontrar algo, a cópia fica bloqueada até você corrigir ou confirmar que revisou.

## O que o DocAnon não garante

**A detecção não é perfeita:** um nome escrito de um jeito incomum, uma sigla interna que só o cliente usa ou uma referência indireta ("a refinaria do litoral norte") podem passar pelos detectores. O perfil da empresa, a revisão e o LLM local reduzem esse risco, mas a sua revisão continua sendo a última barreira.

**O contexto pode identificar:** mesmo sem nomes, uma descrição muito específica pode apontar para uma planta. Um texto que fala de "a única unidade de lubrificantes do estado" identifica a empresa sem citar o nome dela. A auditoria do LLM local ajuda a encontrar esses trechos, mas não substitui o seu julgamento.

**O OCR tem limites:** se um texto da imagem não for lido pelo OCR em nenhuma das três orientações, ele também não é verificado. Isso acontece com texto muito pequeno, com fontes estilizadas e com texto encostado em linhas. Para esses casos, use as regiões fixas do perfil ou desenhe a tarja à mão.

**A escala pode ser descoberta:** se um número escalado puder ser comparado com um valor público conhecido, alguém consegue calcular o fator e desfazer a escala de todos os números da sessão. Troque esses valores por marcadores.

**A IA pode inventar:** a resposta da IA pode conter marcadores que não existem ou números calculados de forma errada. A reidentificação aponta os marcadores desconhecidos e mostra cada número convertido, para que você possa conferir.

**Apagar no SSD não é definitivo:** a exclusão de um cofre sobrescreve o arquivo, mas o disco pode guardar cópias antigas em áreas de reserva. Se isso for importante para você, use criptografia de disco inteiro.

## LGPD

O DocAnon ajuda a reduzir a exposição de dados pessoais antes de um envio a serviços no exterior, mas ele é uma ferramenta, e não uma avaliação jurídica. A decisão de enviar um documento, mesmo anonimizado, continua sendo da sua organização e das políticas dela. Lembre-se também de que dados pseudonimizados continuam sendo dados pessoais para quem tem o mapa, que no caso é o próprio cofre do DocAnon.

## Boas práticas

1. Monte um perfil para cada cliente, com os nomes de plantas, unidades, projetos, produtos e o logotipo.
2. Use uma sessão por conversa com a IA, e não misture clientes na mesma sessão.
3. Revise a lista e as imagens antes de exportar, principalmente os itens desmarcados.
4. Leia a checagem de vazamento. Se ela encontrar algo, corrija antes de liberar a cópia.
5. Prefira a proteção pelo usuário do Windows num computador pessoal e a senha quando precisar abrir os cofres em outra máquina.
6. Mantenha a criptografia de disco ligada.
