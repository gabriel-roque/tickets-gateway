package transaction_pix_handlers

import (
	repository_interfaces "github.com/gabriel-roque/gateway-api/internal/transaction-pix/interfaces"
	repository "github.com/gabriel-roque/gateway-api/internal/transaction-pix/repository"
)

type TransactionPixHandler struct {
	repository repository_interfaces.ITransactionPixRepository
}

func NewTransactionPixHandler() *TransactionPixHandler {
	repository := repository.NewTransactionPixRepository()

	return &TransactionPixHandler{
		repository,
	}
}
