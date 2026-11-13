-- Execute somente no banco antigo, sem recriar/apagar dados.
USE sistema_estoque_ti;
ALTER TABLE pedidos ADD COLUMN id_estoque INT NULL;
ALTER TABLE pedidos ADD CONSTRAINT fk_pedido_estoque FOREIGN KEY (id_estoque) REFERENCES estoque(id_estoque);
-- Pedidos antigos permanecem como estavam. Seus id_estoque ficam NULL.
