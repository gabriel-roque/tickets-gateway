package transaction_pix_handlers

import (
	"errors"

	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
)

func (u *TransactionPixHandler) GetById(id string) (transaction_pix.TransactionPix, error) {
	transaction, err := u.repository.GetById(id)

	if err != nil {
		return transaction_pix.TransactionPix{}, err
	}

	if transaction.Id != id {
		return transaction_pix.TransactionPix{}, errors.New("Not found")
	}

	return transaction, nil
}
