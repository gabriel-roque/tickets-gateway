kind create cluster --config kind-cluster.yaml

kubectl apply -f tickets-api-deployment.yaml

kubectl port-forward svc/tickets-api-service 5000:80

