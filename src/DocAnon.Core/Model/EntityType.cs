namespace DocAnon.Core.Model;

/// <summary>
/// Kinds of sensitive data. The enum name is also the marker prefix written to the
/// anonymized text (<c>[PESSOA_1]</c>, <c>[CPF_2]</c>), so names stay uppercase Portuguese.
/// </summary>
public enum EntityType
{
    // Personal data
    PESSOA,
    ENDERECO,
    CPF,
    CNPJ,
    RG,
    CNH,
    PIS,
    TITULO_ELEITOR,
    CEP,
    TELEFONE,
    EMAIL,
    CARTAO,
    CONTA_BANCARIA,
    PIX,
    PLACA,
    PROCESSO,
    OAB,
    CRM,
    DATA_NASCIMENTO,
    IP,
    SSN,
    PASSAPORTE,
    IBAN,

    // Corporate data
    EMPRESA,
    CLIENTE,
    FORNECEDOR,
    PLANTA,
    UNIDADE,
    LOCAL,
    PROJETO,
    CONTRATO,
    PRODUTO,
    CATALISADOR,
    TAG_EQUIP,
    TAG_INSTR,

    // Numbers with units. They are scaled instead of replaced by a marker,
    // unless the user turns them into a VALOR marker during review.
    QUANTIDADE,
    VALOR,

    PERSONALIZADO,
}

/// <summary>What the anonymizer does with a span that the user kept selected.</summary>
public enum SpanAction
{
    /// <summary>Replace the text with a stable marker such as <c>[PESSOA_1]</c>.</summary>
    Pseudonymize,

    /// <summary>Multiply the number by the session's secret factor.</summary>
    Scale,

    /// <summary>Leave the text as it is. Used for tags and intensive quantities shown in review.</summary>
    Keep,
}

public static class EntityTypeInfo
{
    public static bool IsPersonal(EntityType t) => t <= EntityType.IBAN;

    /// <summary>Types whose value is compared digit by digit, so formatting differences do not create new markers.</summary>
    public static bool IsNumericIdentifier(EntityType t) => t is EntityType.CPF or EntityType.CNPJ or EntityType.RG
        or EntityType.CNH or EntityType.PIS or EntityType.TITULO_ELEITOR or EntityType.CEP or EntityType.TELEFONE
        or EntityType.CARTAO or EntityType.CONTA_BANCARIA or EntityType.PROCESSO or EntityType.SSN
        or EntityType.IBAN or EntityType.PLACA or EntityType.PASSAPORTE;

    public static string Label(EntityType t) => t switch
    {
        EntityType.PESSOA => "Pessoa",
        EntityType.ENDERECO => "Endereço",
        EntityType.TITULO_ELEITOR => "Título de eleitor",
        EntityType.CONTA_BANCARIA => "Conta bancária",
        EntityType.DATA_NASCIMENTO => "Data de nascimento",
        EntityType.TAG_EQUIP => "Tag de equipamento",
        EntityType.TAG_INSTR => "Tag de instrumento",
        EntityType.QUANTIDADE => "Quantidade",
        EntityType.PERSONALIZADO => "Personalizado",
        EntityType.CARTAO => "Cartão",
        EntityType.PROCESSO => "Processo judicial",
        _ => t.ToString()[..1] + t.ToString()[1..].ToLowerInvariant().Replace('_', ' '),
    };
}
