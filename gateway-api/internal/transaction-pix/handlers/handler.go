package transaction_pix_handlers

import (
	repository_interfaces "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/interfaces"
	repository "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/repository"
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
