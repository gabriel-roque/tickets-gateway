package repositories

import "github.com/gabriel-roque/tickets-gateway/internal/entities"

type ICreateCategoryRepository interface {
	Save(category *entities.Category) error
}
