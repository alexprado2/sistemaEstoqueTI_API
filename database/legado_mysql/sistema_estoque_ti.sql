CREATE DATABASE IF NOT EXISTS sistema_estoque_ti CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE sistema_estoque_ti;

CREATE TABLE usuario (
    id_usuario      INT AUTO_INCREMENT PRIMARY KEY,
    nome            VARCHAR(100) NOT NULL,
    email           VARCHAR(120) NOT NULL UNIQUE,
    senha           VARCHAR(255) NOT NULL,
    perfil          ENUM('ADMIN', 'FUNCIONARIO', 'VENDEDOR') NOT NULL,
    ativo           BOOLEAN DEFAULT TRUE
);

CREATE TABLE produto (
    id_produto      INT AUTO_INCREMENT PRIMARY KEY,
    nome            VARCHAR(150) NOT NULL,
    descricao       TEXT,
    preco           DECIMAL(10,2) NOT NULL DEFAULT 0,
    categoria       VARCHAR(100),
    codigo_barras   VARCHAR(50),
    criado_em       TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE estoque (
    id_estoque      INT AUTO_INCREMENT PRIMARY KEY,
    nome_local      VARCHAR(100) NOT NULL,
    descricao       VARCHAR(200)
);

CREATE TABLE estoque_produto (
    id_estoque_produto INT AUTO_INCREMENT PRIMARY KEY,
    id_produto     INT NOT NULL,
    id_estoque     INT NOT NULL,
    quantidade     INT NOT NULL DEFAULT 0,

    CONSTRAINT uq_estoque_produto UNIQUE (id_produto, id_estoque),
    CONSTRAINT fk_ep_produto FOREIGN KEY (id_produto) REFERENCES produto(id_produto),
    CONSTRAINT fk_ep_estoque FOREIGN KEY (id_estoque) REFERENCES estoque(id_estoque)
);

CREATE TABLE movimentacao_estoque (
    id_movimentacao   INT AUTO_INCREMENT PRIMARY KEY,
    id_produto        INT NOT NULL,
    id_estoque        INT NOT NULL,
    id_usuario        INT NOT NULL,
    tipo              ENUM('ENTRADA', 'SAIDA') NOT NULL,
    quantidade        INT NOT NULL,
    data_movimentacao TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_me_produto FOREIGN KEY (id_produto) REFERENCES produto(id_produto),
    CONSTRAINT fk_me_estoque FOREIGN KEY (id_estoque) REFERENCES estoque(id_estoque),
    CONSTRAINT fk_me_usuario FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario)
);

CREATE TABLE pedidos (
    id_pedido       INT AUTO_INCREMENT PRIMARY KEY,
    id_usuario      INT NOT NULL,
    id_estoque      INT NULL,
    data_pedido     TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status          ENUM('ABERTO', 'CONFIRMADO', 'CANCELADO') DEFAULT 'ABERTO',

    CONSTRAINT fk_pedido_usuario FOREIGN KEY (id_usuario) REFERENCES usuario(id_usuario),
    CONSTRAINT fk_pedido_estoque FOREIGN KEY (id_estoque) REFERENCES estoque(id_estoque)
);

CREATE TABLE itens_pedido (
    id_item_pedido   INT AUTO_INCREMENT PRIMARY KEY,
    id_pedido        INT NOT NULL,
    id_produto       INT NOT NULL,
    quantidade       INT NOT NULL,
    preco_unitario   DECIMAL(10,2) NOT NULL,

    CONSTRAINT fk_item_pedido FOREIGN KEY (id_pedido) REFERENCES pedidos(id_pedido),
    CONSTRAINT fk_item_produto FOREIGN KEY (id_produto) REFERENCES produto(id_produto)
);

CREATE INDEX ix_produto_nome ON produto(nome);
CREATE INDEX ix_movimentacao_data ON movimentacao_estoque(data_movimentacao);
CREATE INDEX ix_pedidos_status ON pedidos(status);

-- As contas de demonstração (admin@sistema.com, estoque@sistema.com,
-- vendas@sistema.com) são inicializadas pela API quando SeedDemoUsers=true.
-- Em Production, configure suas senhas fortes via variáveis DemoUsers__*.
-- A API só cria contas ausentes; não altera contas existentes.

-- Controle de sessões e chaves de integração.
CREATE TABLE IF NOT EXISTS api_sessoes (
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
    id_usuario INT NOT NULL,
    criada_em_utc DATETIME(6) NOT NULL,
    expira_em_utc DATETIME(6) NOT NULL,
    INDEX ix_api_sessoes_usuario (id_usuario),
    INDEX ix_api_sessoes_expiracao (expira_em_utc),
    CONSTRAINT fk_api_sessoes_usuario FOREIGN KEY (id_usuario)
        REFERENCES usuario(id_usuario) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS api_chaves (
    id_chave BIGINT AUTO_INCREMENT PRIMARY KEY,
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
    id_usuario INT NOT NULL,
    nome VARCHAR(100) NOT NULL,
    escopo ENUM('LEITURA', 'ESCRITA_PEDIDOS') NOT NULL,
    criada_em_utc DATETIME(6) NOT NULL,
    expira_em_utc DATETIME(6) NOT NULL,
    revogada_em_utc DATETIME(6) NULL,
    INDEX ix_api_chaves_usuario (id_usuario),
    INDEX ix_api_chaves_expiracao (expira_em_utc),
    CONSTRAINT fk_api_chaves_usuario FOREIGN KEY (id_usuario)
        REFERENCES usuario(id_usuario) ON DELETE CASCADE
) ENGINE=InnoDB;
