---
layout: default
title: Instalação
nav_order: 2
---

# Instalação
{: .no_toc }

1. TOC
{:toc}

## Requisitos

| Componente | Observação |
|---|---|
| **Windows 10 ou 11, 64 bits** | O OCR usa bibliotecas nativas do Tesseract compiladas para Windows x64. |
| **Espaço em disco** | O instalador tem cerca de 215 MB, e a instalação ocupa cerca de 500 MB. |
| **Memória** | Documentos de texto usam pouca memória. Desenhos A1 usam bem mais, porque o OCR roda em três orientações ao mesmo tempo. |
| **Ollama** _(opcional)_ | Só é necessário para as sugestões do LLM local. Veja [LLM local](llm-local.md). |

O instalador já leva o runtime do .NET, o modelo de reconhecimento de nomes e os dados do Tesseract em português e inglês. Por isso, o computador de destino não precisa de mais nada nem de acesso à internet.

## Instalador

Baixe `DocAnon-<versão>-win-x64-setup.exe` da página de [releases](https://github.com/DanWBR/DocAnon/releases) e rode o arquivo. O instalador pergunta se a instalação será para todos os usuários ou só para você:

- **Para todos os usuários:** o DocAnon vai para `C:\Program Files\DocAnon`, e o instalador pede direitos de administrador.
- **Só para você:** o DocAnon vai para a sua pasta de perfil, e o instalador não pede direitos de administrador.

Na tela de tarefas, há duas opções desmarcadas por padrão:

- **Ícone na Área de Trabalho:** cria um atalho para o aplicativo.
- **Adicionar a linha de comando ao PATH:** permite chamar `docanon` de qualquer terminal. Se você marcar essa opção, a desinstalação tira a pasta do PATH de novo.

### Instalação silenciosa

Se você precisar instalar sem janelas, por exemplo em vários computadores, use os parâmetros do Inno Setup:

```powershell
.\DocAnon-0.1.0-win-x64-setup.exe /VERYSILENT /CURRENTUSER /TASKS="clipath"
```

Sem `/CURRENTUSER`, a instalação vai para todos os usuários e precisa de um terminal com direitos de administrador.

## Onde ficam os dados

O DocAnon guarda três tipos de arquivo em `%LOCALAPPDATA%\DocAnon`:

| Caminho | Conteúdo | Cifrado |
|---|---|---|
| `sessions\*.docanon` | o mapa entre marcadores e valores reais de cada sessão | sim |
| `profiles\*.docprofile` | os perfis de empresa | sim |
| `settings.json` | as preferências do aplicativo | não, porque não contém dados de documentos |

O DocAnon não guarda cópias dos documentos que você abre. Os arquivos exportados só são gravados quando você clica em **Salvar pacote** e escolhe uma pasta.

## Desinstalação

Use **Adicionar ou remover programas** no Windows. A desinstalação remove o aplicativo, os modelos e os atalhos, mas deixa `%LOCALAPPDATA%\DocAnon` como está, porque os cofres guardam o mapa das conversas que você já fez com a IA. Se você quiser apagar também os cofres e os perfis, apague essa pasta depois de desinstalar.

## Compilar a partir do código

Se você quiser compilar o DocAnon, precisa do .NET 10 SDK, do Python 3 com `onnx` e `onnxruntime` e do Inno Setup 6. Os passos estão no [README do repositório](https://github.com/DanWBR/DocAnon#compilação). O script `tools\fetch-models.ps1` baixa os modelos e prepara o modelo de NER. Depois, o script `packaging\windows\build-installer.ps1` publica o aplicativo e a linha de comando, confere se os modelos e as bibliotecas nativas estão no pacote e gera o instalador em `dist\`.

O GitHub Actions gera o mesmo instalador a cada push para `master`. Quando uma tag `v<versão>` é enviada, ele também publica o instalador numa release.
