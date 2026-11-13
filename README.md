# Sistema de Estoque TI — API

API criada para um **PIM (Projeto Integrado Multidisciplinar)** com o objetivo de controlar produtos de TI, locais de estoque, movimentações e pedidos.

## 1. Tecnologias utilizadas

| Tecnologia | Utilização |
|---|---|
| **C# / .NET 8** | Linguagem e plataforma do projeto. |
| **ASP.NET Core (Controllers)** | Desenvolvimento das rotas da API. |
| **PostgreSQL** | Armazenamento dos dados. |
| **Dapper + Npgsql** | Consultas SQL e conexão com PostgreSQL. |
| **Docker** | Execução da API em contêiner. |
| **Render** | Hospedagem da API e do banco na nuvem. |

A API tem uma estrutura baseada em **MVC**: `Controllers` tratam as requisições, `Models` representam os dados e a interface (View) fica em projetos separado.

## 2. Funcionalidades

- **Login:** permite entrar no sistema e encerrar a sessão.
- **Dashboard:** mostra indicadores gerais do estoque e dos pedidos.
- **Produtos:** cadastrar, listar, pesquisar, editar e excluir.
- **Estoques:** gerenciar os locais onde os produtos ficam armazenados.
- **Saldo de produtos:** consultar quantidades por produto e local.
- **Movimentações:** registrar entradas e saídas, mantendo um histórico.
- **Pedidos:** cadastrar, consultar e cancelar pedidos.
- **Usuários:** gerenciar contas e níveis de acesso.
- **Integrações:** gerar chaves para outros sistemas acessarem a API.

## 3. Comunicação e protocolos

A API segue o padrão **REST**, utiliza **HTTP/HTTPS** para comunicação e troca informações no formato **JSON**.

| Método | Finalidade |
|---|---|
| `GET` | Consultar dados. |
| `POST` | Cadastrar dados ou fazer login. |
| `PUT` | Atualizar dados ou cancelar pedidos. |
| `DELETE` | Excluir dados ou revogar chaves. |

O **HTTPS** protege os dados durante a transmissão. A API utiliza códigos de resposta como `200` (sucesso), `201` (criado), `400` (requisição inválida), `401` (não autenticado), `403` (sem permissão), `404` (não encontrado), `409` (conflito) e `429` (excesso de tentativas).

## 4. Segurança

- **Autenticação por API Key:** o login gera uma chave que deve ser enviada no cabeçalho `X-API-Key`.
- **Expiração:** as chaves de login valem **30 dias** e são revogadas no logout.
- **Proteção de senhas:** utiliza **PBKDF2 com SHA-256 e salt**.
- **Proteção das chaves:** no banco, as API Keys são armazenadas somente como **hash SHA-256**.
- **Permissões por perfil:** limita as ações de cada usuário.
- **CORS:** permite requisições do navegador somente das origens configuradas.
- **Rate Limiting:** limita login e criação de chaves de integração a **10 tentativas por minuto por IP**.
- **Consultas parametrizadas e transações:** ajudam a prevenir SQL Injection e inconsistências no estoque.

### Perfis

| Perfil | O que pode fazer |
|---|---|
| **ADMIN** | Gerenciar usuários, produtos, estoques, movimentações, pedidos e integrações. |
| **FUNCIONARIO** | Consultar dados, cadastrar/editar estoques e registrar entradas e saídas. |
| **VENDEDOR** | Consultar dados e criar/cancelar os próprios pedidos. |

Para **outras aplicações**, o administrador pode criar chaves de integração com acesso de `LEITURA` ou `ESCRITA_PEDIDOS`, válidas de **1 a 365 dias**. Essas chaves possuem acesso limitado, sem funções administrativas.

## 5. Rotas disponíveis

| Método | Rota | Função |
|---|---|---|
| POST | `/api/auth/login` | Login e geração da chave. |
| POST | `/api/auth/logout` | Encerrar sessão. |
| GET | `/api/auth/me` | Consultar usuário logado. |
| GET | `/api/dashboard/resumo` | Indicadores do dashboard. |
| GET / POST | `/api/produtos` | Listar/pesquisar e cadastrar produtos. |
| GET / PUT / DELETE | `/api/produtos/{id}` | Consultar, editar e excluir produto. |
| GET / POST | `/api/estoques` | Listar e cadastrar estoques. |
| PUT / DELETE | `/api/estoques/{id}` | Editar e excluir estoque. |
| GET | `/api/estoque-produtos` | Consultar saldos por produto/local. |
| GET / POST | `/api/movimentacoes` | Listar e registrar movimentações. |
| GET / POST | `/api/pedidos` | Listar e criar pedidos. |
| GET | `/api/pedidos/{id}` | Consultar pedido e seus itens. |
| PUT | `/api/pedidos/{id}/cancelar` | Cancelar pedido e restituir estoque. |
| GET / POST | `/api/usuarios` | Listar e cadastrar usuários (ADMIN). |
| PUT / DELETE | `/api/usuarios/{id}` | Editar e desativar usuário (ADMIN). |
| GET / POST | `/api/integracoes/chaves` | Listar e criar chaves (ADMIN). |
| DELETE | `/api/integracoes/chaves/{id}` | Revogar chave (ADMIN). |
| GET | `/health` | Verificar funcionamento da API. |


<!-- ## 6. Exemplos de uso

**Fazer login:**

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "admin@sistema.com",
  "senha": "SENHA_CONFIGURADA"
}
```

A resposta traz o usuário, a `token` (API Key) e a data de expiração.

**Consultar produtos com a chave recebida:**

```http
GET /api/produtos
X-API-Key: SUA_CHAVE_DE_LOGIN
```

**Criar um pedido:**

```http
POST /api/pedidos
Content-Type: application/json
X-API-Key: SUA_CHAVE_DE_LOGIN

{
  "idEstoque": 1,
  "itens": [
    { "idProduto": 1, "quantidade": 2, "precoUnitario": 150.00 }
  ]
}
```

O pedido é confirmado quando há estoque suficiente. Caso contrário, a API retorna **409** e informa o produto com estoque insuficiente. -->

## 6. Como executar

**É necessário ter Docker e acesso a um banco PostgreSQL.**

1. Crie um banco chamado `sistema_estoque_ti` no PostgreSQL.
2. Copie `.env.example` para `.env` e configure `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER` e `DB_PASSWORD`.
3. Configure `WEB_ORIGINS` com o endereço do WEB (ex.: `http://127.0.0.1:5500,http://127.0.0.1:5501`).
4. Na pasta da API, execute:

```bash
docker compose up --build -d
```

5. Teste em **`http://localhost:5080/health`**. O endereço base da API será `http://localhost:5080`.

Ao iniciar, a API cria automaticamente as tabelas que faltam usando `database/postgresql.sql`, sem apagar os dados existentes. **Somente a API roda no Docker**; o WEB e o PostgreSQL ficam separados.

### Contas de demonstração

Com `SEED_DEMO_USERS=true`, são criadas as contas ausentes:

| Perfil | E-mail |
|---|---|
| ADMIN | `admin@sistema.com` |
| FUNCIONARIO | `estoque@sistema.com` |
| VENDEDOR | `vendas@sistema.com` |

Em **Development**, contas novas usam `123456` se nenhuma senha for configurada. Em **Production**, configure senhas próprias de **12 ou mais caracteres** nas variáveis de ambiente. Não use senhas de teste em sistemas públicos.

## 7. Publicação e estrutura

O `Dockerfile` prepara a API para execução, e o `render.yaml` configura a publicação no **Render** com PostgreSQL. No servidor, configure as credenciais e `FrontendOrigins` com a URL do WEB. As credenciais não devem ser colocadas no código-fonte público.

```text
SistemaEstoqueTI_API/
├── Controllers/       # Endpoints
├── Models/            # Modelos e dados
├── Infrastructure/    # Banco, segurança e validações
├── database/          # SQL de criação das tabelas
├── Program.cs         # Configuração da API
├── Dockerfile          # Contêiner da API
├── docker-compose.yml  # Execução local
└── render.yaml         # Publicação no Render
```

**Em resumo:** o projeto oferece uma API REST em C# e PostgreSQL para gerenciar estoque e pedidos, com autenticação por API Key, controle de permissões.
