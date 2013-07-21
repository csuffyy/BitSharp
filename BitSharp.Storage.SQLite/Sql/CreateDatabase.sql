CREATE TABLE IF NOT EXISTS Blocks
(
	BlockHash BINARY(32) NOT NULL,
	PreviousBlockHash BINARY(32) NOT NULL,
	RawBytes VARBINARY(100000000) NOT NULL,
	CONSTRAINT PK_Blocks PRIMARY KEY
	(
		BlockHash ASC
	)
);

CREATE INDEX IF NOT EXISTS IX_Blocks_PreviousBlockHash ON Blocks ( PreviousBlockHash );

CREATE TABLE IF NOT EXISTS ChainedBlocks
(
	BlockHash BINARY(32) NOT NULL,
	PreviousBlockHash BINARY(32) NOT NULL,
	Height INT NOT NULL,
	TotalWork BINARY(64) NOT NULL,
	CONSTRAINT PK_ChainedBlocks PRIMARY KEY
	(
		BlockHash ASC
	)
);

CREATE INDEX IF NOT EXISTS IX_ChainedBlocks_PreviousBlockHash ON ChainedBlocks ( PreviousBlockHash );

CREATE TABLE IF NOT EXISTS KnownAddresses
(
	IPAddress BINARY(16) NOT NULL,
	Port BINARY(2) NOT NULL,
	Services BINARY(8) NOT NULL,
	Time BINARY(4) NOT NULL,
	CONSTRAINT PK_KnownAddresses PRIMARY KEY
	(
		IPAddress ASC,
		Port ASC
	)
);

CREATE TABLE IF NOT EXISTS TransactionLocators
(
	BlockHash BINARY(32) NOT NULL,
	TransactionHash BINARY(32) NOT NULL,
	TransactionIndex BINARY(4) NOT NULL,
	CONSTRAINT PK_TransactionLocators PRIMARY KEY
	(
		BlockHash,
		TransactionHash
	)
);

CREATE INDEX IF NOT EXISTS IX_TransactionLocators_TransactionHash ON TransactionLocators ( TransactionHash );
