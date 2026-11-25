---
layout: default
title: Sessões e cofre
nav_order: 9
---

# Sessões e cofre
{: .no_toc }

1. TOC
{:toc}

## Sessões

Uma sessão corresponde a uma conversa com a IA. Se você enviar o relatório e o desenho de um mesmo estudo na mesma conversa, abra os dois na mesma sessão. Dentro de uma sessão:

- o mesmo valor recebe sempre o mesmo marcador, em qualquer documento;
- os números são escalados pelos mesmos fatores;
- um valor anonimizado num documento é procurado em todos os documentos seguintes.

Se você começar uma conversa nova com a IA sobre outro assunto, crie uma sessão nova. Sessões separadas têm marcadores e fatores independentes, e por isso a IA não consegue cruzar informações de conversas diferentes.

## O que o cofre guarda

Cada sessão é um arquivo `.docanon` em `%LOCALAPPDATA%\DocAnon\sessions`. O arquivo contém:

- o mapa entre cada marcador e os valores reais, com todas as variantes encontradas;
- os dois fatores de escala;
- o par entre cada número escalado e o número original, para que os números repetidos pela IA voltem exatos;
- a lista dos documentos que passaram pela sessão;
- a referência ao perfil de empresa usado.

O cofre não guarda o conteúdo dos documentos.

## Criptografia

O arquivo é cifrado com AES-256-GCM, que também autentica o conteúdo. Se alguém alterar um único byte do arquivo, ele deixa de abrir. A chave pode vir de duas fontes, escolhidas em **Configurações**:

**Usuário do Windows:** o DocAnon gera uma chave aleatória para cada arquivo e a protege com o DPAPI do Windows. O cofre só abre com o seu usuário naquele computador, e você não precisa digitar senha. Essa é a opção padrão.

**Senha:** a chave vem da senha, pelo PBKDF2-SHA256 com 600 mil iterações e sal aleatório. O cofre abre em qualquer computador com a senha, o que é útil se você usar o DocAnon em mais de uma máquina. A senha é digitada na barra de cima e fica só na memória enquanto o aplicativo estiver aberto. Se você esquecer a senha, os cofres feitos com ela não abrem mais.

Os perfis de empresa usam a mesma proteção, em arquivos `.docprofile` na pasta `profiles`.

## Rótulo da sessão

O rótulo aparece no nome do arquivo do cofre sem cifra, para que você possa escolher a sessão na lista sem abrir o cofre. Por isso, use rótulos neutros, como `estudo-tocha-2026` ou `contrato-revisao-03`, e não use o nome do cliente nem outro nome sigiloso.

## Quando a sessão é salva

O DocAnon salva a sessão quando ela é criada e a cada exportação. Os marcadores que aparecem só na revisão, como `[PESSOA_novo]`, ainda não existem no cofre; eles passam a existir quando você clica em **Anonimizar e verificar vazamentos**.

## Apagar uma sessão

O botão **Apagar** sobrescreve o arquivo com bytes aleatórios antes de removê-lo. Em discos SSD, o controlador do disco pode manter cópias antigas dos blocos em áreas de reserva, e por isso a sobrescrita não garante a remoção física. Se isso for importante, use criptografia de disco inteiro, como o BitLocker.

Depois que uma sessão é apagada, as respostas da IA daquela conversa não podem mais ser reidentificadas.
