---
layout: default
title: Guia da interface
nav_order: 4
---

# Guia da interface
{: .no_toc }

1. TOC
{:toc}

A janela tem três partes: a barra de sessão e perfil em cima, cinco abas no meio e a barra de status embaixo.

## Barra de sessão e perfil

| Controle | O que faz |
|---|---|
| **Sessão** (lista) | mostra as sessões salvas no cofre, pelo rótulo |
| **Abrir** | abre a sessão escolhida na lista |
| **Apagar** | apaga o cofre da sessão escolhida, sobrescrevendo o arquivo antes de removê-lo |
| **rótulo da nova sessão** e **Nova sessão** | criam uma sessão nova, com fatores de escala sorteados na hora |
| **senha do cofre** | só aparece quando os cofres usam senha; veja [Sessões e cofre](sessoes.md) |
| **perfis de empresa** (lista), **Usar** e **Sem perfil** | escolhem o perfil aplicado aos próximos documentos |

O texto no meio da barra mostra a sessão aberta, quantas entidades ela tem e quantos documentos já passaram por ela.

## 1. Documentos

Nesta aba você entrega os documentos ao DocAnon.

- **Arraste documentos para cá:** aceita vários arquivos de uma vez. A lista de formatos aparece logo abaixo do título.
- **Escolher arquivos:** abre o seletor de arquivos do Windows.
- **Ou cole um texto:** analisa um texto que esteja em outro programa, como um e-mail.
- **Documentos desta sessão:** lista os documentos abertos, com o estado de cada um e os avisos da leitura, como uma imagem com OCR de baixa confiança.

As duas primeiras opções só funcionam depois que uma sessão foi criada ou aberta.

## 2. Revisão

A revisão é a parte mais importante do uso, porque é nela que você decide o que sai do computador.

### Lista "O que será alterado"

Cada linha agrupa todas as ocorrências de um mesmo valor no documento.

| Elemento | Significado |
|---|---|
| caixa de marcação | se estiver marcada, todas as ocorrências serão alteradas |
| tipo | Pessoa, Cpf, Empresa, Unidade, Tag de equipamento, Quantidade e assim por diante |
| texto e `×N` | o valor encontrado e quantas vezes ele aparece |
| `→` | o que vai entrar no lugar: um marcador como `[PESSOA_1]`, `[PESSOA_novo]` quando o valor ainda não está na sessão, o número escalado ou "mantido" |
| ação (só em quantidades) | **escalar**, **manter** ou **marcador** |
| origem | o detector que encontrou o valor: regex, ner, nomes, perfil, sessao, propagacao, tag, quantidade, llm ou manual |

Se você parar o mouse sobre uma linha, aparece a explicação do detector, como "dígito verificador ok" ou "tag genérica, mantida por padrão".

Os itens são criados marcados ou desmarcados de acordo com a confiança. Tags genéricas, siglas curtas, nomes com baixa confiança e sugestões do LLM começam desmarcados, e você decide se eles entram.

### Sub-aba Texto

O texto do documento aparece em Markdown, com cores por categoria:

| Cor | Categoria |
|---|---|
| vermelho | dados pessoais |
| azul | empresas, clientes, projetos e produtos |
| verde | plantas, unidades, locais e endereços |
| roxo | tags de equipamentos e instrumentos |
| amarelo | números escalados ou trocados por marcador |
| cinza claro | trechos encontrados que ficarão como estão |

Quando você seleciona um trecho e clica com o botão direito, aparecem três opções:

- **Anonimizar seleção como:** marca o trecho com o tipo escolhido. A marca manual vence qualquer detector, e o DocAnon procura o mesmo valor no resto do documento.
- **Adicionar ao perfil da empresa como:** grava o termo no perfil, para que ele seja anonimizado também nos próximos documentos, e marca o trecho no documento atual.
- **Nunca anonimizar este termo:** grava o termo nas exceções do perfil e desmarca todas as ocorrências.

### Sub-aba Imagens

Esta sub-aba aparece com conteúdo quando o documento tem desenhos, páginas escaneadas ou imagens embutidas.

- A lista do alto escolhe a página ou a imagem.
- **Exportar esta imagem:** decide se ela sai como imagem tarjada. Imagens muito pequenas, como ícones e logotipos colados num DOCX, começam desmarcadas.
- **Vermelho:** palavras que vão receber um marcador.
- **Azul:** tarjas sem texto, como logotipos encontrados, regiões fixas do perfil e imagens pequenas do desenho. Um clique liga ou desliga cada uma.
- **Preto:** tarjas que você desenhou. Para desenhar, arraste com o botão esquerdo. Para apagar, clique com o botão direito sobre a tarja.

### Botão Anonimizar e verificar vazamentos

O botão aplica as escolhas da revisão, gera o texto e as imagens, roda a checagem de vazamento, salva a sessão no cofre e abre a aba de exportação.

## 3. Exportação

| Controle | O que faz |
|---|---|
| **Copiar texto** | copia o Markdown anonimizado para a área de transferência |
| **Salvar pacote** | grava numa pasta o Markdown, um PNG por imagem, um PDF só com as imagens e, para arquivos Visio, uma cópia `.vsdx` anonimizada |
| **Checagem de vazamento** | lista cada valor real que ainda aparece, com o motivo |
| **Revisei os pontos acima e quero liberar a cópia** | libera a cópia quando a checagem encontrou pontos que você considera aceitáveis |
| **Imagens tarjadas** e **Copiar imagem** | mostram cada imagem e copiam uma de cada vez para colar na IA |

Os botões de cópia ficam bloqueados enquanto a checagem tiver pontos abertos e a caixa de confirmação estiver desmarcada.

## 4. Reidentificação

1. Cole a resposta da IA no campo da esquerda.
2. Clique em **Restaurar valores reais**.
3. Copie o texto da direita com **Copiar texto restaurado**.

A lista de baixo mostra cada marcador restaurado e cada número convertido. O DocAnon reconhece os marcadores mesmo quando a IA muda a forma deles, como `[Pessoa 1]`, `PESSOA_1`, `**[PESSOA_1]**` ou a versão em inglês `[PERSON_1]`. Se a IA criar um marcador que não existe na sessão, ele aparece como **MARCADOR DESCONHECIDO** e fica como está no texto.

A reidentificação usa a sessão aberta. Por isso, abra a mesma sessão em que o documento foi anonimizado.

## 5. Configurações

### Perfil da empresa

O lado esquerdo edita o perfil carregado: nome, termos, padrões, exceções, padrões de prefixo de unidade, opções de tags e de imagens pequenas, logotipos e regiões fixas. O botão **Salvar perfil** grava tudo no cofre. O botão **Exportar perfil...** grava uma cópia cifrada com senha, para backup, e **Importar perfil...** lê uma cópia exportada ou um perfil em JSON. A página [Perfis de empresa](perfis.md) explica cada campo.

### Detecção

- **Reconhecimento de nomes:** liga o modelo de NER local.
- **Lista de prenomes comuns:** ajuda a encontrar nomes soltos em carimbos e tabelas.
- **Resolução das imagens (dpi):** controla a resolução das páginas renderizadas para OCR e para as imagens tarjadas. O padrão é 200 dpi.

### Escala dos números

O intervalo define onde os fatores secretos das novas sessões são sorteados. O padrão é de 0,40 a 2,50, e o sorteio evita a faixa de 0,80 a 1,25, porque um fator perto de 1 quase não esconde nada.

### Cofres

- **windows:** o cofre só abre com o seu usuário do Windows. É a opção mais prática para uso num único computador.
- **senha:** o cofre abre em qualquer computador com a senha, que você digita na barra de cima.

### LLM local (Ollama)

Liga o Ollama, define o endereço (sempre local) e escolhe os modelos de texto e de visão. O botão **Testar e listar modelos** confere a conexão e preenche as listas. A página [LLM local](llm-local.md) traz os modelos recomendados.

O botão **Salvar configurações** grava as preferências e recarrega os modelos no próximo documento.

## Barra de status

A barra de baixo mostra o que o DocAnon está fazendo, o andamento e o resultado da última ação. Enquanto um trabalho estiver rodando, aparecem a barra de progresso e o botão **Cancelar**.
