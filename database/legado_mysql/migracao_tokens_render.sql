-- Migração idempotente: execute no MySQL 8 caso o usuário da API não tenha
-- permissão CREATE TABLE. Não altera nem apaga registros existentes.
-- Escolha antes o banco correto: USE sistema_estoque_ti;
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
