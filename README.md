# Notification Relay

Aplicativo que captura as notificações do Windows e as envia em tempo real para o seu celular Android via Firebase Cloud Messaging (FCM).

## Como funciona

- O lado PC fica na bandeja do sistema e escuta todas as notificações do Windows (Discord, Slack, etc.)
- Quando uma notificação chega, ela é enviada via FCM para o celular
- O app Android recebe a notificação, salva no banco de dados local (Room) e exibe agrupada por aplicativo de origem

## Requisitos

- Windows 10/11
- .NET 8
- Windows SDK (`makeappx.exe` e `signtool.exe`) em `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64`
- Conta Firebase com um projeto criado e `serviceAccountKey.json` gerado

## Instalação (PC)

1. Clone o repositório
2. Coloque o `serviceAccountKey.json` do Firebase em `%LOCALAPPDATA%\NotificationRelay\`
3. Crie o ficheiro `%LOCALAPPDATA%\NotificationRelay\.env` com o token FCM do seu telemóvel:
   ```
   FCM_DEVICE_TOKEN=seu_token_aqui
   ```
4. Execute o script de instalação no PowerShell (como administrador):
   ```powershell
   .\Register-Package.ps1
   ```
5. Abra o app pelo menu Iniciar pesquisando "Notification Relay"

O app inicia automaticamente com o Windows após a instalação.

## Instalação (Android)

Abra o projeto Android em Android Studio e instale no seu dispositivo.
