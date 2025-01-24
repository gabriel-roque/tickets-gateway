package transaction_pix_repository

import (
	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
)

func (r *Repository) GetById(id string) transaction_pix.TransactionPix {
	var transaction transaction_pix.TransactionPix

	queryGet := `
		SELECT id, name, external_id, value, qr_code
		FROM transaction_pix
		WHERE id = $1
	`

	row := r.db.QueryRow(queryGet, id)
	row.Scan(
		&transaction.Id, &transaction.Name,
		&transaction.ExternalId, &transaction.Value,
		&transaction.QrCode,
	)

	return transaction
}
