---
layout: default
title: Primeiros passos
nav_order: 3
---

# Primeiros passos
{: .no_toc }

Esta página mostra um uso completo do DocAnon: você anonimiza um relatório técnico, envia o texto a um modelo de IA e restaura os valores reais na resposta.

1. TOC
{:toc}

## 1. Crie um perfil da empresa

Antes do primeiro documento, vale a pena criar um perfil com os nomes sigilosos do cliente. O detector de nomes reconhece pessoas e empresas, mas ele não tem como saber que "Projeto Boreal" ou "Unidade de Hidrotratamento II" são nomes internos que precisam ser escondidos.

1. Abra a aba **5. Configurações**.
2. Clique em **Novo perfil** e dê um nome a ele, por exemplo "Cliente X".
3. Clique em **Adicionar termo** para cada nome sigiloso. Em cada linha, escreva o termo, escolha o tipo (Empresa, Cliente, Planta, Unidade, Projeto, Produto) e, se houver, escreva as variantes separadas por ponto e vírgula.
4. Clique em **Salvar perfil**. O perfil é salvo cifrado.
5. Na barra de cima, escolha o perfil na lista da direita e clique em **Usar**.

A página [Perfis de empresa](perfis.md) explica as exceções, os logotipos e as regiões fixas.

## 2. Crie uma sessão

Uma sessão corresponde a uma conversa com a IA. Todos os documentos da mesma sessão usam os mesmos marcadores e os mesmos fatores de escala, de modo que a IA vê um quadro coerente.

1. Na barra de cima, escreva um rótulo em **rótulo da nova sessão**, por exemplo `estudo-tocha-2026`.
2. Clique em **Nova sessão**.

O rótulo fica no nome do arquivo do cofre sem cifra, por isso não use o nome do cliente nem outro nome sigiloso nele.

## 3. Abra o documento

Na aba **1. Documentos**, arraste o arquivo para a área indicada ou clique em **Escolher arquivos**. Se o texto estiver em outro programa, cole-o no campo **Ou cole um texto** e clique em **Analisar texto colado**.

O DocAnon lê o documento e procura os dados sensíveis. Num relatório comum, isso leva alguns segundos. Num desenho A1 sem camada de texto, o OCR pode levar perto de um minuto. A barra de status mostra o andamento, e o botão **Cancelar** interrompe o trabalho.

## 4. Revise

Quando a leitura termina, o DocAnon abre a aba **2. Revisão**. Do lado esquerdo fica o texto do documento com os trechos coloridos, e do lado direito fica a lista **O que será alterado**.

- Cada linha da lista mostra o tipo, o texto encontrado, quantas vezes ele aparece e o que vai entrar no lugar. Se a caixa estiver marcada, o trecho será alterado. Se estiver desmarcada, ele fica como está.
- Nas quantidades, a lista oferece três ações: **escalar** (multiplica pelo fator secreto), **manter** e **marcador** (troca o número por `[VALOR_1]`).
- Se um nome sigiloso não foi encontrado, selecione-o no texto, clique com o botão direito e escolha **Anonimizar seleção como**. O DocAnon marca esse trecho em todo o documento.
- Se um termo técnico foi marcado sem necessidade, selecione-o e escolha **Nunca anonimizar este termo**. O termo entra nas exceções do perfil.
- Se o documento tiver desenhos ou imagens, abra a sub-aba **Imagens** e confira as tarjas. A página [Desenhos e imagens](imagens.md) explica as cores e como desenhar tarjas à mão.

## 5. Exporte

Clique em **Anonimizar e verificar vazamentos**. O DocAnon aplica os marcadores e a escala, cobre o texto sensível das imagens e confere o resultado. A aba **3. Exportação** mostra:

- o texto anonimizado em Markdown;
- a lista da checagem de vazamento;
- as imagens tarjadas.

Se a checagem não encontrar problemas, os botões **Copiar texto**, **Salvar pacote** e **Copiar imagem** ficam liberados. Se ela encontrar algum ponto, volte à revisão e corrija. Se você conferir os pontos e decidir que eles não são sigilosos, marque **Revisei os pontos acima e quero liberar a cópia**.

## 6. Converse com a IA

Cole o texto no ChatGPT, no Claude ou em outro modelo, junto com as imagens se houver. Faça a pergunta normalmente. Os marcadores e os números escalados não atrapalham a IA, porque o texto continua coerente. Ao pedir a resposta, vale a pena orientar a IA a manter os marcadores como estão, por exemplo com "use os marcadores entre colchetes exatamente como aparecem no texto".

## 7. Restaure a resposta

Copie a resposta da IA, abra a aba **4. Reidentificação**, cole a resposta e clique em **Restaurar valores reais**. O DocAnon troca cada marcador pelo valor real e divide os números escalados pelos fatores da sessão. A lista abaixo do texto mostra cada troca. Se a IA inventou um marcador que não existe na sessão, ele aparece como **MARCADOR DESCONHECIDO**.

Se a IA calculou um número novo a partir dos números escalados, como uma vazão total, o DocAnon também converte esse número de volta, desde que ele venha com uma unidade que o DocAnon reconheça. Confira esses valores na lista, porque eles foram calculados pela IA.

## 8. Continue a conversa

Se você abrir outro documento para a mesma conversa, abra-o na mesma sessão. O DocAnon reconhece os nomes que já foram anonimizados nos documentos anteriores e usa os mesmos marcadores. Se você fechar o aplicativo, a sessão continua salva no cofre: escolha-a na lista **Sessão** e clique em **Abrir**.
