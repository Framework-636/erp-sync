-- Stand-in ERP schema (issue #6).
--
-- Safe to run more than once: each table is created only if it doesn't exist yet.
--
-- Deliberately shaped like a legacy ERP, not a modern app:
--   * Keys are codes a person typed (ItemCode), not auto-numbers.
--   * The item table is wide, and some columns are clutter the bridge will never read.
--   * DateUpdated is a date with no time, so "what changed in the last 15 seconds?"
--     can't be answered from it. That's why the bridge fingerprints rows instead.

IF OBJECT_ID(N'dbo.Item', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Item
    (
        -- Identity
        ItemCode            NVARCHAR(30)   NOT NULL,

        -- What it is
        Description         NVARCHAR(100)  NOT NULL,
        ExtendedDescription NVARCHAR(500)  NULL,
        ProductLine         NVARCHAR(10)   NOT NULL,
        Category            NVARCHAR(30)   NOT NULL,
        Brand               NVARCHAR(50)   NULL,
        UnitOfMeasure       NVARCHAR(10)   NOT NULL CONSTRAINT DF_Item_UnitOfMeasure DEFAULT (N'EA'),
        CasePack            INT            NOT NULL CONSTRAINT DF_Item_CasePack      DEFAULT (1),
        UpcCode             NVARCHAR(14)   NULL,

        -- Size and money
        Weight              DECIMAL(10, 3) NULL,
        Length              DECIMAL(10, 2) NULL,
        Width               DECIMAL(10, 2) NULL,
        Height              DECIMAL(10, 2) NULL,
        StandardCost        DECIMAL(19, 4) NOT NULL CONSTRAINT DF_Item_StandardCost  DEFAULT (0),
        ListPrice           DECIMAL(19, 4) NOT NULL CONSTRAINT DF_Item_ListPrice     DEFAULT (0),

        -- Purchasing
        VendorNumber        NVARCHAR(15)   NULL,
        VendorItemCode      NVARCHAR(30)   NULL,

        -- Flags: A = active, I = inactive, D = discontinued
        Status              CHAR(1)        NOT NULL CONSTRAINT DF_Item_Status        DEFAULT ('A'),
        Taxable             BIT            NOT NULL CONSTRAINT DF_Item_Taxable       DEFAULT (1),

        -- Clutter the bridge will not read
        Notes               NVARCHAR(MAX)  NULL,
        UserUpdated         NVARCHAR(20)   NULL,
        DateCreated         DATE           NOT NULL CONSTRAINT DF_Item_DateCreated   DEFAULT (CAST(GETDATE() AS DATE)),
        DateUpdated         DATE           NULL,

        CONSTRAINT PK_Item          PRIMARY KEY (ItemCode),
        CONSTRAINT CK_Item_Status   CHECK (Status IN ('A', 'I', 'D')),
        CONSTRAINT CK_Item_CasePack CHECK (CasePack >= 1)
    );
END;

IF OBJECT_ID(N'dbo.ItemWarehouse', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ItemWarehouse
    (
        ItemCode                NVARCHAR(30)   NOT NULL,
        WarehouseCode           NVARCHAR(4)    NOT NULL,

        QuantityOnHand          DECIMAL(16, 3) NOT NULL CONSTRAINT DF_ItemWarehouse_OnHand      DEFAULT (0),
        QuantityOnSalesOrder    DECIMAL(16, 3) NOT NULL CONSTRAINT DF_ItemWarehouse_OnSO        DEFAULT (0),
        QuantityOnPurchaseOrder DECIMAL(16, 3) NOT NULL CONSTRAINT DF_ItemWarehouse_OnPO        DEFAULT (0),
        QuantityBackordered     DECIMAL(16, 3) NOT NULL CONSTRAINT DF_ItemWarehouse_Backordered DEFAULT (0),
        ReorderPoint            DECIMAL(16, 3) NOT NULL CONSTRAINT DF_ItemWarehouse_Reorder     DEFAULT (0),
        BinLocation             NVARCHAR(10)   NULL,
        LastSoldDate            DATE           NULL,
        LastReceiptDate         DATE           NULL,

        CONSTRAINT PK_ItemWarehouse      PRIMARY KEY (ItemCode, WarehouseCode),
        CONSTRAINT FK_ItemWarehouse_Item FOREIGN KEY (ItemCode)
            REFERENCES dbo.Item (ItemCode) ON DELETE CASCADE,
        CONSTRAINT CK_ItemWarehouse_WarehouseCode CHECK (WarehouseCode IN (N'MAIN', N'EAST', N'WEST'))
    );
END;
